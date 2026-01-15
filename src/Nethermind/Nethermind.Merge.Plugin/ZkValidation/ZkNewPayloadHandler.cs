// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.ZkValidation.EthProofValidator;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.Merge.Plugin.ZkValidation;

/// <summary>
/// Provides a (ZK Validation edit) payload handler as defined in Engine API.
/// <a href="https://github.com/ethereum/execution-apis/blob/main/src/engine/shanghai.md#engine_newpayloadv2">
/// Shanghai</a> specification.
/// </summary>
public sealed class ZkNewPayloadHandler : IAsyncHandler<ExecutionPayload, PayloadStatusV1>
{
    private readonly IBlockTree _blockTree;
    private readonly ILogger _logger;
    private readonly BlockValidator _blockValidator;

    public ZkNewPayloadHandler(IBlockTree blockTree, ILogManager logManager)
    {
        _blockTree = blockTree;
        _logger = logManager.GetClassLogger();
        _blockValidator = new BlockValidator(_logger);
        if (_logger.IsInfo) _logger.Info($"ZK Validation strategy initialized.");
    }

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
            return NewPayloadV1Result.Invalid(null,
                $"Block {request} could not be parsed as a block: {decodingResult.Error}");
        }

        if (_logger.IsInfo) _logger.Info($"ZK Validation triggered for block {block.Number} ({block.Hash})...");

        (ValidationResult result, string? message) = await ValidateAsync(block);

        return result switch
        {
            ValidationResult.Valid => NewPayloadV1Result.Valid(block.Hash),
            ValidationResult.Invalid => NewPayloadV1Result.Invalid(null, message),
            _ => NewPayloadV1Result.Syncing
        };
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
                AddBlockResult addResult = await _blockTree.SuggestBlockAsync(block, BlockTreeSuggestOptions.ForceDontSetAsMain);

                if (addResult == AddBlockResult.InvalidBlock) return (ValidationResult.Invalid, "Block rejected by BlockTree");

                _blockTree.UpdateMainChain(new[] { block }, wereProcessed: true, forceHeadBlock: true);

                return (ValidationResult.Valid, null);
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
