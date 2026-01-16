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
using BlockValidator = Nethermind.Merge.Plugin.ZkValidation.EthProofValidator.BlockValidator;

namespace Nethermind.Merge.Plugin.ZkValidation;

/// <summary>
/// Simplified newPayload handler for ZK validation mode.
/// </summary>
public sealed class ZkNewPayloadHandler(
    IBlockCacheService blockCacheService,
    IInvalidChainTracker invalidChainTracker,
    ILogManager logManager)
    : IAsyncHandler<ExecutionPayload, PayloadStatusV1>
{
    private readonly ILogger _logger = logManager.GetClassLogger();
    private readonly BlockValidator _blockValidator = new(logManager);

    /// <summary>
    /// Validate via ZK the payload and returns the <see cref="PayloadStatusV1"/>
    /// and the hash of the last valid block.
    /// </summary>
    /// <param name="request">The execution payload to validate.</param>
    /// <returns></returns>
    public async Task<ResultWrapper<PayloadStatusV1>> HandleAsync(ExecutionPayload request)
    {
        BlockDecodingResult decodingResult = request.TryGetBlock();
        Block? block = decodingResult.Block;
        if (block is null)
        {
            if (_logger.IsTrace) _logger.Trace($"New Block Request Invalid: {decodingResult.Error} ; {request}.");
            return NewPayloadV1Result.Invalid(null, $"Block {request} could not be parsed as a block: {decodingResult.Error}");
        }

        if (_logger.IsInfo) _logger.Info($"[ZK] Received New Block:  {request}");

        if (!HeaderValidator.ValidateHash(block!.Header, out Hash256 actualHash))
        {
            if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, "invalid block hash"));
            return NewPayloadV1Result.Invalid(null, $"Invalid block hash {request.BlockHash} does not match calculated hash {actualHash}.");
        }

        invalidChainTracker.SetChildParent(block.Hash!, block.ParentHash!);
        if (invalidChainTracker.IsOnKnownInvalidChain(block.Hash!, out Hash256? lastValidHash))
        {
            if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, $"block is a part of an invalid chain") + $". The last valid is {lastValidHash}");
            return NewPayloadV1Result.Invalid(lastValidHash, $"Block {request} is known to be a part of an invalid chain.");
        }

        (ValidationResult result, string? message) = await ValidateAsync(block);
        switch (result)
        {
            case ValidationResult.Invalid:
            {
                invalidChainTracker.OnInvalidBlock(block.Hash!, block.ParentHash);
                return NewPayloadV1Result.Invalid(null, message);
            }
            case ValidationResult.Valid:
                return NewPayloadV1Result.Valid(block.Hash);
            case ValidationResult.Syncing:
                return NewPayloadV1Result.Syncing;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task<(ValidationResult, string?)> ValidateAsync(Block block)
    {
        BlockValidator.ZkValidationResult result = await _blockValidator.ValidateBlockAsync(block.Number);

        switch (result)
        {
            case BlockValidator.ZkValidationResult.Invalid:
            {
                if (_logger.IsWarn) _logger.Warn($"Block {block.Number} failed ZK validation.");
                return (ValidationResult.Invalid, "ZK Proof verification failed.");
            }
            case BlockValidator.ZkValidationResult.Unavailable:
            {
                if (_logger.IsWarn) _logger.Warn($"Block {block.Number}, no proofs available.");
                return (ValidationResult.Syncing, "Proofs not available.");
            }
            case BlockValidator.ZkValidationResult.Valid:
            {
                blockCacheService.BlockCache.TryAdd(block.Hash!, block);
                if (_logger.IsInfo) _logger.Info($"[ZK] Block {block.Number} valid cached");
                return (ValidationResult.Valid, null);
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private enum ValidationResult
    {
        Invalid,
        Valid,
        Syncing
    }
}
