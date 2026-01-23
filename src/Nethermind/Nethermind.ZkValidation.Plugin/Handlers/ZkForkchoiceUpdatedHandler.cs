// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;
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
        // Attester-Only: Explicitly reject block production requests
        if (payloadAttributes is not null)
            return ResultWrapper<ForkchoiceUpdatedV1Result>.Fail("Block production is disabled for ZK Stateless mode.");

        Hash256 headBlockHash = forkchoiceState.HeadBlockHash;

        if (validationService.IsOnInvalidChain(headBlockHash, out Hash256? lastValidHash))
        {
            if (_logger.IsWarn) _logger.Warn($"Received Invalid {forkchoiceState} - {headBlockHash} is known to be a part of an invalid chain.");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Invalid(lastValidHash));
        }

        if (!validationService.TryGet(headBlockHash, out Block block))
        {
            if (_logger.IsInfo) _logger.Info($"Syncing Unknown ForkChoiceState head hash Request: {forkchoiceState}.");
            return Task.FromResult(ForkchoiceUpdatedV1Result.Syncing);
        }

        string requestStr = forkchoiceState.ToString(block.Number);
        if (_logger.IsInfo) _logger.Info($"Received {requestStr}");

        if (blockTree.Head?.Hash != headBlockHash)
        {
            // We need to fake TD for the block to be inserted as the head
            block.Header.TotalDifficulty = UInt256.Parse("58750000000000000000000");
            blockTree.Insert(block, BlockTreeInsertBlockOptions.SaveHeader, BlockTreeInsertHeaderOptions.BeaconBlockInsert);
            blockTree.UpdateMainChain(new[] { block }, true, true);
            if (_logger.IsInfo) _logger.Info($"Synced Chain Head to {block.ToString(Block.Format.Short)}");
        }

        blockTree.ForkChoiceUpdated(forkchoiceState.FinalizedBlockHash, forkchoiceState.SafeBlockHash);
        return Task.FromResult(ForkchoiceUpdatedV1Result.Valid(null, headBlockHash));
    }
}
