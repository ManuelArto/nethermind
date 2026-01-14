// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.Merge.Plugin.Handlers.Strategies
{
    public class ZkValidationStrategy : IPayloadExecutionStrategy
    {
        private readonly IBlockTree _blockTree;
        private readonly ILogger _logger;

        public ZkValidationStrategy(IBlockTree blockTree, ILogManager logManager)
        {
            _blockTree = blockTree;
            _logger = logManager.GetClassLogger();
        }

        public async Task<(ValidationResult, string?)> ExecuteAsync(Block block, BlockHeader parent, ProcessingOptions options)
        {
            if (_logger.IsInfo) _logger.Info($"ZK Validation triggered (MOCK) for block {block.Number} ({block.Hash})");

            // 1. Add to Tree (if not already present)
            AddBlockResult addResult = await _blockTree.SuggestBlockAsync(block, BlockTreeSuggestOptions.ForceDontSetAsMain);

            if (addResult == AddBlockResult.InvalidBlock)
            {
                return (ValidationResult.Invalid, "Block rejected by BlockTree");
            }

            // 2. Mark as Processed and Update Head
            // In a real ZK scenario, we would verify the proof here.
            // Since this is a mock, we assume validity and bypass EVM execution.
            // We directly update the main chain to reflect that this block is now the head and is "processed".
            
            // Note: This bypasses the BlockchainProcessor entirely.
            // Side effects: State is not actually written to DB (unless we have a Witness/Diff mechanism).
            // But for a "Stateless" verifier, this is the intended behavior: accept the block based on proof.
            
            _blockTree.UpdateMainChain(new[] { block }, wereProcessed: true, forceHeadBlock: true);

            return (ValidationResult.Valid, null);
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
