// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.ZkValidation.EthProofValidator;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.Merge.Plugin.ZkValidation
{
    public class ZkValidationStrategy : IPayloadExecutionStrategy
    {
        private readonly IBlockTree _blockTree;
        private readonly ILogger _logger;
        private readonly BlockValidator _blockValidator;

        public ZkValidationStrategy(IBlockTree blockTree, ILogManager logManager)
        {
            _blockTree = blockTree;
            _logger = logManager.GetClassLogger();
            _blockValidator = new BlockValidator(_logger);
            _ = _blockValidator.InitializeAsync();
            if (_logger.IsInfo) _logger.Info($"ZK Validation strategy initialized.");
        }

        public async Task<(NewPayloadHandler.ValidationResult, string?)> ExecuteAsync(Block block, BlockHeader parent, ProcessingOptions options)
        {
            if (_logger.IsInfo) _logger.Info($"ZK Validation triggered for block {block.Number} ({block.Hash})...");

            bool isValid = await _blockValidator.ValidateBlockAsync(block.Number);

            if (!isValid)
            {
                 if (_logger.IsWarn) _logger.Warn($"Block {block.Number} failed ZK validation.");
                 return (NewPayloadHandler.ValidationResult.Invalid, "ZK Proof verification failed.");
            }

            AddBlockResult addResult = await _blockTree.SuggestBlockAsync(block, BlockTreeSuggestOptions.ForceDontSetAsMain);

            if (addResult == AddBlockResult.InvalidBlock)
            {
                return (NewPayloadHandler.ValidationResult.Invalid, "Block rejected by BlockTree");
            }

            _blockTree.UpdateMainChain(new[] { block }, wereProcessed: true, forceHeadBlock: true);

            return (NewPayloadHandler.ValidationResult.Valid, null);
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
