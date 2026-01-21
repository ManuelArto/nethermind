// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Concurrent;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Attributes;
using Nethermind.Core.Crypto;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using BlockValidator = Nethermind.Merge.Plugin.ZkValidation.EthProofValidator.BlockValidator;

namespace Nethermind.Merge.Plugin.ZkValidation;

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
    // TODO: refactor with blockcCacheService
    private readonly ConcurrentDictionary<Hash256, Block> _pendingBlocks = new();

    private const int RetryDelayMs = 4000;
    private const int MaxRetries = 5;

    /// <summary>
    /// Validate a block via ZK proof. Returns immediately.
    /// If proofs are unavailable, starts background retry and returns Pending.
    /// </summary>
    public async Task<string> ValidateAsync(Block block)
    {
        if (blockCacheService.BlockCache.ContainsKey(block.Hash!))
        {
            if (_logger.IsInfo) _logger.Info($"Block {block.Number} already validated.");
            return PayloadStatus.Valid;
        }

        if (_pendingBlocks.ContainsKey(block.Hash!))
            return PayloadStatus.Syncing;

        string result = await _blockValidator.ValidateBlockAsync(block.Number);
        HandleResult(block, result);
        return result;
    }

    public bool IsOnInvalidChain(Block block, out Hash256? lastValidHash)
    {
        invalidChainTracker.SetChildParent(block.Hash!, block.ParentHash!);
        if (!invalidChainTracker.IsOnKnownInvalidChain(block.Hash!, out lastValidHash)) return false;
        if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, "block is on an invalid chain"));
        return true;
    }

    private void HandleResult(Block block, string result, int retry = 0)
    {
        if (result == PayloadStatus.Valid)
        {
            blockCacheService.BlockCache.TryAdd(block.Hash!, block);
            _pendingBlocks.TryRemove(block.Hash!, out _);
            if (_logger.IsInfo)
            {
                _logger.Info($"Block {block.Number} validated and cached (retry: {retry})");
            }
            return;
        }

        if (result == PayloadStatus.Invalid)
        {
            invalidChainTracker.OnInvalidBlock(block.Hash!, block.ParentHash);
            _pendingBlocks.TryRemove(block.Hash!, out _);
            if (_logger.IsWarn)
            {
                _logger.Warn($"Block {block.Number} failed ZK validation (retry: {retry})");
            }
            return;
        }

        // Syncing
        if (!_pendingBlocks.TryAdd(block.Hash!, block)) return;
        if (_logger.IsInfo)
            _logger.Info($"Block {block.Number} proofs unavailable. Start validation in background...");
        _ = RetryInBackgroundAsync(block);
    }

    private async Task RetryInBackgroundAsync(Block block)
    {
        for (int retry = 1; retry <= MaxRetries; retry++)
        {
            await Task.Delay(RetryDelayMs);
            if (_logger.IsDebug) _logger.Debug($"Retry {retry}/{MaxRetries} for block {block.Number}...");

            string result = await _blockValidator.ValidateBlockAsync(block.Number);
            HandleResult(block, result);

            if (result != PayloadStatus.Syncing) return;
        }

        // Timeout
        _pendingBlocks.TryRemove(block.Hash!, out _);
        if (_logger.IsWarn) _logger.Warn($"Block {block.Number} timed out after {MaxRetries} retries.");
    }
}
