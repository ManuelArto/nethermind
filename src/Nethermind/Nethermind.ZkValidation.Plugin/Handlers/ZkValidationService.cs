// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using ConcurrentCollections;
using Nethermind.Core;
using Nethermind.Core.Caching;
using Nethermind.Core.Crypto;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using Nethermind.ZkValidation.Plugin.EthProofValidator;

namespace Nethermind.ZkValidation.Plugin.Handlers;

/// <summary>
/// Service that handles ZK proof validation with background retry support.
/// </summary>
public class ZkValidationService(IInvalidChainTracker invalidChainTracker, IBlockValidator blockValidator, ILogManager logManager)
{
    private readonly ILogger _logger = logManager.GetClassLogger();
    private readonly IBlockValidator _blockValidator = blockValidator;

    private readonly LruCache<Hash256, Block> _validBlocks = new(128, "ZkValidBlocks");
    private readonly ConcurrentHashSet<Hash256> _pendingBlocks = [];

    private const int RetryDelayMs = 1000;
    private const int MaxRetries = 10;

    /// <summary>
    /// Validate a block via ZK proof. Returns immediately.
    /// If proofs are unavailable, starts background retry and returns Pending.
    /// </summary>
    public async Task<string> ValidateAsync(Block block)
    {
        if (_validBlocks.TryGet(block.Hash!, out _))
        {
            if (_logger.IsInfo) _logger.Info($"Block {block.Number} already validated.");
            return PayloadStatus.Valid;
        }
        if (_pendingBlocks.Contains(block.Hash!)) return PayloadStatus.Syncing;

        string result = await _blockValidator.ValidateBlockAsync(block.Number);
        HandleResult(block, result);
        return result;
    }

    public bool TryGet(Hash256 blockHash, out Block? block)
    {
        return _validBlocks.TryGet(blockHash, out block);
    }

    public bool IsOnInvalidChain(Hash256 blockHash, out Hash256? lastValidHash, Hash256? parentHash = null)
    {
        if (parentHash is not null) invalidChainTracker.SetChildParent(blockHash, parentHash);
        return invalidChainTracker.IsOnKnownInvalidChain(blockHash, out lastValidHash);
    }

    private void HandleResult(Block block, string result, int retry = 0)
    {
        if (result == PayloadStatus.Valid)
        {
            _validBlocks.Set(block.Hash!, block);
            _pendingBlocks.TryRemove(block.Hash!);
            if (_logger.IsInfo)
            {
                _logger.Info($"Block {block.Number} validated and cached (retry: {retry})");
                if (block.Number % 10 == 0) LogCacheStats();
            }
            return;
        }

        if (result == PayloadStatus.Invalid)
        {
            invalidChainTracker.OnInvalidBlock(block.Hash!, block.ParentHash);
            _pendingBlocks.TryRemove(block.Hash!);
            if (_logger.IsWarn) _logger.Warn($"Block {block.Number} failed ZK validation (retry: {retry})");
            return;
        }

        // Syncing
        if (!_pendingBlocks.Add(block.Hash!)) return;
        if (_logger.IsInfo) _logger.Info($"Block {block.Number} proofs unavailable. Start validation in background...");
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
        _pendingBlocks.TryRemove(block.Hash!);
        if (_logger.IsWarn) _logger.Warn($"Block {block.Number} timed out after {MaxRetries} retries.");
    }

    private void LogCacheStats()
    {
        long sizeBytes = _validBlocks.MemorySize;
        double sizeMb = sizeBytes / (1024.0 * 1024.0);
        _logger.Info($"ZK Cache Stats: {_validBlocks.Count}/128 blocks | Est. RAM: {sizeMb:N2} MB");
    }
}
