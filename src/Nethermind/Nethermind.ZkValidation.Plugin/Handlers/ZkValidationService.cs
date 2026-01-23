// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using ConcurrentCollections;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Logging;
using Nethermind.Merge.Plugin;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.InvalidChainTracker;
using Nethermind.ZkValidation.Plugin.EthProofValidator;

namespace Nethermind.ZkValidation.Plugin.Handlers;

/// <summary>
/// Service that handles ZK proof validation with background retry support.
/// </summary>
public class ZkValidationService(
    IBlockValidator blockValidator,
    IBlockTree blockTree,
    IInvalidChainTracker invalidChainTracker,
    ILogManager logManager)
{
    private readonly ILogger _logger = logManager.GetClassLogger();

    private readonly ConcurrentHashSet<Hash256> _pendingBlocks = [];

    private const int RetryDelayMs = 2000;
    private const int MaxRetries = 10;

    /// <summary>
    /// Validate a block via ZK proof. Returns immediately.
    /// If proofs are unavailable, starts background retry and returns Pending.
    /// </summary>
    public async Task<string> ValidateAsync(Block block)
    {
        if (blockTree.IsOnMainChainBehindOrEqualHead(block))
        {
            if (_logger.IsInfo) _logger.Info($"Valid... A new payload ignored. Block {block.ToString(Block.Format.Short)} found in main chain.");
            return PayloadStatus.Valid;
        }

        if (_pendingBlocks.Contains(block.Hash!)) {
            if (_logger.IsInfo) _logger.Info($"Syncing... Block already known {block}.");
            return PayloadStatus.Syncing;
        }

        string result = await blockValidator.ValidateBlockAsync(block.Number);
        HandleResult(block, result);
        return result;
    }

    private void HandleResult(Block block, string result, int retry = 0)
    {
        if (result == PayloadStatus.Valid)
        {
            if (_logger.IsInfo) _logger.Info($"Block {block.Number} is valid (retry: {retry})");
            blockTree.Insert(block,
                BlockTreeInsertBlockOptions.SaveHeader | BlockTreeInsertBlockOptions.SkipCanAcceptNewBlocks,
                BlockTreeInsertHeaderOptions.BeaconBlockInsert | BlockTreeInsertHeaderOptions.MoveToBeaconMainChain);
            blockTree.MarkChainAsProcessed(new[] { block });
            _pendingBlocks.TryRemove(block.Hash!);
            return;
        }

        if (result == PayloadStatus.Invalid)
        {
            if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, $"block is invalid according to ZK proof validation (retry: {retry})"));
            invalidChainTracker.OnInvalidBlock(block.Hash!, block.ParentHash);
            _pendingBlocks.TryRemove(block.Hash!);
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

            string result = await blockValidator.ValidateBlockAsync(block.Number);
            HandleResult(block, result, retry);

            if (result != PayloadStatus.Syncing) return;
        }

        // Timeout
        _pendingBlocks.TryRemove(block.Hash!);
        if (_logger.IsWarn) _logger.Warn($"Block {block.Number} timed out after {MaxRetries} retries.");
    }
}
