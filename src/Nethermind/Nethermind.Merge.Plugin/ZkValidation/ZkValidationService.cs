// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Concurrent;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using BlockValidator = Nethermind.Merge.Plugin.ZkValidation.EthProofValidator.BlockValidator;

namespace Nethermind.Merge.Plugin.ZkValidation;

public enum ZkBlockStatus { Valid, Invalid, Pending }

/// <summary>
/// Service that handles ZK proof validation with background retry support.
/// </summary>
public class ZkValidationService(
    IBlockCacheService blockCacheService,
    IInvalidChainTracker invalidChainTracker,
    ILogManager logManager)
{
    private readonly ILogger _logger = logManager.GetClassLogger();
    private readonly BlockValidator _blockValidator = new(logManager);
    private readonly ConcurrentDictionary<Hash256, Block> _pendingBlocks = new();

    private const int RetryDelayMs = 2000;
    private const int MaxRetries = 30;

    /// <summary>
    /// Check if a block has already been validated (cached) or is pending validation.
    /// </summary>
    public ZkBlockStatus? GetBlockStatus(Hash256 blockHash)
    {
        if (blockCacheService.BlockCache.ContainsKey(blockHash))
            return ZkBlockStatus.Valid;
        if (_pendingBlocks.ContainsKey(blockHash))
            return ZkBlockStatus.Pending;

        return null;
    }

    /// <summary>
    /// Validate a block via ZK proof. Returns immediately.
    /// If proofs are unavailable, starts background retry and returns Pending.
    /// </summary>
    public async Task<ZkBlockStatus> ValidateAsync(Block block)
    {
        BlockValidator.ZkValidationResult result = await _blockValidator.ValidateBlockAsync(block.Number);
        return HandleResult(block, result, isRetry: false);
    }

    private ZkBlockStatus HandleResult(Block block, BlockValidator.ZkValidationResult result, bool isRetry, int retryCount = 0)
    {
        switch (result)
        {
            case BlockValidator.ZkValidationResult.Valid:
                blockCacheService.BlockCache.TryAdd(block.Hash!, block);
                _pendingBlocks.TryRemove(block.Hash!, out _);
                if (_logger.IsInfo)
                {
                    _logger.Info(isRetry
                        ? $"[ZK] Block {block.Number} ✅ validated after {retryCount} retries."
                        : $"[ZK] Block {block.Number} ✅ validated and cached.");
                }

                return ZkBlockStatus.Valid;

            case BlockValidator.ZkValidationResult.Invalid:
                invalidChainTracker.OnInvalidBlock(block.Hash!, block.ParentHash);
                _pendingBlocks.TryRemove(block.Hash!, out _);
                if (_logger.IsWarn)
                {
                    _logger.Warn(isRetry
                        ? $"[ZK] Block {block.Number} ❌ invalid after {retryCount} retries."
                        : $"[ZK] Block {block.Number} ❌ failed ZK validation.");
                }

                return ZkBlockStatus.Invalid;

            case BlockValidator.ZkValidationResult.Unavailable:
                if (isRetry || !_pendingBlocks.TryAdd(block.Hash!, block)) return ZkBlockStatus.Pending;

                if (_logger.IsInfo) _logger.Info($"[ZK] Block {block.Number} proofs unavailable. Starting background retry...");
                _ = RetryInBackgroundAsync(block);
                return ZkBlockStatus.Pending;

            default:
                return ZkBlockStatus.Invalid;
        }
    }

    private async Task RetryInBackgroundAsync(Block block)
    {
        for (int retry = 1; retry <= MaxRetries; retry++)
        {
            await Task.Delay(RetryDelayMs);
            if (_logger.IsDebug) _logger.Debug($"[ZK] Retry {retry}/{MaxRetries} for block {block.Number}...");

            BlockValidator.ZkValidationResult result = await _blockValidator.ValidateBlockAsync(block.Number);
            ZkBlockStatus status = HandleResult(block, result, isRetry: true, retryCount: retry);

            if (status != ZkBlockStatus.Pending)
                return;
        }

        // Timeout
        _pendingBlocks.TryRemove(block.Hash!, out _);
        if (_logger.IsWarn) _logger.Warn($"[ZK] Block {block.Number} ⚠️ timed out after {MaxRetries} retries.");
    }
}
