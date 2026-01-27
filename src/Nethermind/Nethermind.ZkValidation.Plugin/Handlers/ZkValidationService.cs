// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Logging;
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

    // private readonly LruCache<Hash256, Block> _validBlocks = new(128, "ZkValidBlocks");

    private const int RetryDelayMs = 1000;
    private const int MaxRetries = 8;

    /// <summary>
    /// Validate a block via ZK proof. Returns immediately.
    /// This method will WAIT until the proofs are ready or timeout occurs.
    /// </summary>
    public async Task<string> ValidateAsync(Block block)
    {
        Block? inserted = blockTree.FindBlock(block.Hash!, BlockTreeLookupOptions.DoNotCreateLevelIfMissing);
        if (inserted is not null)
        {
            if (_logger.IsInfo) _logger.Info($"Valid. Block {block.Number} already validated.");
            return PayloadStatus.Valid;
        }

        // We hold the RPC thread here to ensure we validate block before CL sends FCU.
        // TODO: extract in function, call once waiting, then for lower time continue on the background
        for (int retry = 0; retry <= MaxRetries; retry++)
        {
            if (retry > 0) await Task.Delay(RetryDelayMs);

            // TODO: Validate one block before to avoid optimistic sync (for now)
            string result = await blockValidator.ValidateBlockAsync(block.Number - 1);

            if (result == PayloadStatus.Valid)
            {
                blockTree.Insert(block, BlockTreeInsertBlockOptions.SaveHeader, BlockTreeInsertHeaderOptions.BeaconBlockInsert);
                if (_logger.IsInfo)
                {
                    _logger.Info($"Valid. Block {block.Number} (retries {retry})");
                    if (block.Number % 10 == 0) LogCacheStats();
                }

                return PayloadStatus.Valid;
            }

            if (result == PayloadStatus.Invalid)
            {
                if (_logger.IsWarn)
                    _logger.Warn($"Invalid. Block {block.Number} failed ZK validation (retries {retry})");
                invalidChainTracker.OnInvalidBlock(block.Hash!, block.ParentHash);
                return PayloadStatus.Invalid;
            }
        }

        // If we reach here, the prover is slower than our validation window. CL will go "Optimistic".
        if (_logger.IsWarn) _logger.Warn($"Syncing. Block {block.Number} timed out after {MaxRetries} retries.");
        return PayloadStatus.Syncing;
    }

    // public bool TryGet(Hash256 blockHash, out Block block)
    // {
    //     return _validBlocks.TryGet(blockHash, out block);
    // }

    public bool IsOnInvalidChain(Hash256 blockHash, out Hash256? lastValidHash, Hash256? parentHash = null)
    {
        if (parentHash is not null) invalidChainTracker.SetChildParent(blockHash, parentHash);
        return invalidChainTracker.IsOnKnownInvalidChain(blockHash, out lastValidHash);
    }

    private void LogCacheStats()
    {
        //long sizeBytes = _validBlocks.MemorySize;
        //double sizeKb = sizeBytes / 1024.0;
        //if (_logger.IsWarn)
        //    _logger.Warn($"[ZK] Cache Stats: {_validBlocks.Count}/128 blocks | Est. RAM: {sizeKb:N2} KB");
    }
}
