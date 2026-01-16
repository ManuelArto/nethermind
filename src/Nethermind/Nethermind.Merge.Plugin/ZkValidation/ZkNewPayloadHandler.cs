// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading.Tasks;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.InvalidChainTracker;

namespace Nethermind.Merge.Plugin.ZkValidation;

/// <summary>
/// Simplified newPayload handler for ZK validation mode.
/// Delegates ZK validation to <see cref="ZkValidationService"/>.
/// </summary>
public sealed class ZkNewPayloadHandler(
    ZkValidationService validationService,
    IInvalidChainTracker invalidChainTracker,
    ILogManager logManager)
    : IAsyncHandler<ExecutionPayload, PayloadStatusV1>
{
    private readonly ILogger _logger = logManager.GetClassLogger();

    /// <summary>
    /// Validate via ZK the payload and returns the <see cref="PayloadStatusV1"/>
    /// and the hash of the last valid block.
    /// </summary>
    /// <param name="request">The execution payload to validate.</param>
    /// <returns></returns>
    public async Task<ResultWrapper<PayloadStatusV1>> HandleAsync(ExecutionPayload request)
    {
        Block? block = DecodeBlock(request);
        if (block is null)
            return NewPayloadV1Result.Invalid(null, $"Block {request} could not be parsed.");

        if (_logger.IsInfo) _logger.Info($"[ZK] Received New Block:  {request}");

        if (!ValidateBlockHash(block, out Hash256 actualHash))
            return NewPayloadV1Result.Invalid(null, $"Invalid block hash {request.BlockHash} does not match {actualHash}.");

        if (IsOnInvalidChain(block, out Hash256? lastValidHash))
            return NewPayloadV1Result.Invalid(lastValidHash, $"Block {request} is on an invalid chain.");

        // Check cached/pending status first
        ZkBlockStatus? cachedStatus = validationService.GetBlockStatus(block.Hash!);
        if (cachedStatus == ZkBlockStatus.Valid)
        {
            if (_logger.IsInfo) _logger.Info($"[ZK] Block {block.Number} already validated.");
            return NewPayloadV1Result.Valid(block.Hash);
        }
        if (cachedStatus == ZkBlockStatus.Pending)
        {
            if (_logger.IsInfo) _logger.Info($"[ZK] Block {block.Number} validation in progress...");
            return NewPayloadV1Result.Syncing;
        }

        // Validate via ZK
        ZkBlockStatus status = await validationService.ValidateAsync(block);
        return status switch
        {
            ZkBlockStatus.Valid => NewPayloadV1Result.Valid(block.Hash),
            ZkBlockStatus.Invalid => NewPayloadV1Result.Invalid(null, "ZK Proof verification failed."),
            ZkBlockStatus.Pending => NewPayloadV1Result.Syncing,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Block? DecodeBlock(ExecutionPayload request)
    {
        BlockDecodingResult result = request.TryGetBlock();
        if (result.Block is null && _logger.IsTrace)
            _logger.Trace($"New Block Request Invalid: {result.Error} ; {request}.");
        return result.Block;
    }

    private bool ValidateBlockHash(Block block, out Hash256 actualHash)
    {
        if (HeaderValidator.ValidateHash(block.Header, out actualHash)) return true;
        if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, "invalid block hash"));
        return false;
    }

    private bool IsOnInvalidChain(Block block, out Hash256? lastValidHash)
    {
        invalidChainTracker.SetChildParent(block.Hash!, block.ParentHash!);
        if (!invalidChainTracker.IsOnKnownInvalidChain(block.Hash!, out lastValidHash)) return false;
        if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, "block is on an invalid chain"));
        return true;
    }
}
