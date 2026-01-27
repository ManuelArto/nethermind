// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.ZkValidation.Plugin.Handlers;

/// <summary>
/// Simplified forkchoice update handler for ZK validation mode.
/// Trusts ZK validation done in newPayload.
/// </summary>
public class ZkForkchoiceUpdatedHandler(
    ZkValidationService validationService,
    IBlockTree blockTree,
    ILogManager logManager)
    : IForkchoiceUpdatedHandler
{
    private readonly ILogger _logger = logManager.GetClassLogger();

    public Task<ResultWrapper<ForkchoiceUpdatedV1Result>> Handle(
        ForkchoiceStateV1 forkchoiceState,
        PayloadAttributes? payloadAttributes,
        int version)
    {
        Hash256 headBlockHash = forkchoiceState.HeadBlockHash;

        if (validationService.IsOnInvalidChain(headBlockHash, out Hash256? lastValidHash))
        {
            if (_logger.IsWarn) _logger.Warn($"Received Invalid {forkchoiceState} - {headBlockHash} is known to be a part of an invalid chain.");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Invalid(lastValidHash));
        }

        Block? block = blockTree.FindBlock(headBlockHash, BlockTreeLookupOptions.DoNotCreateLevelIfMissing);
        if (block is null)
        {
            if (_logger.IsInfo) _logger.Info($"Syncing Unknown ForkChoiceState head hash Request: {forkchoiceState}.");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Syncing);
        }

        Hash256 safeBlockHash = forkchoiceState.SafeBlockHash;
        Hash256 finalizedBlockHash = forkchoiceState.FinalizedBlockHash;

        BlockHeader? safeBlockHeader = ValidateBlockHash(ref safeBlockHash, out string? safeBlockErrorMsg);
        BlockHeader? finalizedHeader = ValidateBlockHash(ref finalizedBlockHash, out string? finalizationErrorMsg);

        string requestStr = forkchoiceState.ToString(block.Number, safeBlockHeader?.Number, finalizedHeader?.Number);
        if (_logger.IsInfo) _logger.Info($"Received {requestStr}");

        if (finalizationErrorMsg is not null)
        {
            if (_logger.IsWarn) _logger.Warn($"Finalized {finalizationErrorMsg}.");
            return ForkchoiceUpdatedV1Result.Syncing;
        }

        if (safeBlockErrorMsg is not null)
        {
            if (_logger.IsWarn) _logger.Warn($"Safe {safeBlockErrorMsg}.");
            return ForkchoiceUpdatedV1Result.Syncing;
        }

        if (blockTree.Head?.Hash != headBlockHash)
        {
            blockTree.UpdateMainChain(new[] { block }, true, true);
            if (_logger.IsInfo) _logger.Info($"Synced Chain Head to {block.ToString(Block.Format.Short)}");
        }

        blockTree.ForkChoiceUpdated(forkchoiceState.FinalizedBlockHash, forkchoiceState.SafeBlockHash);
        return Task.FromResult(ForkchoiceUpdatedV1Result.Valid(null, headBlockHash));
    }

    protected virtual BlockHeader? ValidateBlockHash(ref Hash256 blockHash, out string? errorMessage, bool skipZeroHash = true)
    {
        errorMessage = null;
        if (skipZeroHash && blockHash == Keccak.Zero)
        {
            return null;
        }

        BlockHeader? blockHeader = blockTree.FindHeader(blockHash, BlockTreeLookupOptions.DoNotCreateLevelIfMissing);
        if (blockHeader is null)
        {
            errorMessage = $"Block {blockHash} not found.";
        }
        return blockHeader;
    }
}
