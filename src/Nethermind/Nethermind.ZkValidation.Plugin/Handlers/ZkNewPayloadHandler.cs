// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading.Tasks;
using Nethermind.Consensus;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.ZkValidation.Plugin.Handlers;

/// <summary>
/// Simplified newPayload handler for ZK validation mode.
/// Delegates ZK validation to <see cref="ZkValidationService"/>.
/// </summary>
public sealed class ZkNewPayloadHandler(ZkValidationService validationService, IPoSSwitcher poSSwitcher, ILogManager logManager)
    : IAsyncHandler<ExecutionPayload, PayloadStatusV1>
{
    private readonly ILogger _logger = logManager.GetClassLogger();

    private long _lastBlockNumber;
    private long _lastBlockGasLimit;

    /// <summary>
    /// Validate via ZK the payload and returns the <see cref="PayloadStatusV1"/>
    /// and the hash of the last valid block.
    /// </summary>
    /// <param name="request">The execution payload to validate.</param>
    /// <returns></returns>
    public async Task<ResultWrapper<PayloadStatusV1>> HandleAsync(ExecutionPayload request)
    {
        BlockDecodingResult decodingResult = request.TryGetBlock(poSSwitcher.FinalTotalDifficulty);
        Block? block = decodingResult.Block;
        if (block is null)
        {
            if (_logger.IsTrace) _logger.Trace($"New Block Request Invalid: {decodingResult.Error} ; {request}.");
            return NewPayloadV1Result.Invalid(null, $"Block {request} could not be parsed as a block: {decodingResult.Error}");
        }

        string requestStr = $"New Block:  {request}";
        if (_logger.IsInfo)
        {
            _logger.Info($"Received {requestStr}      | limit {block.Header.GasLimit,13:N0} {GetGasChange(block.Number == _lastBlockNumber + 1 ? block.Header.GasLimit : _lastBlockGasLimit)}");
            _lastBlockNumber = block.Number;
            _lastBlockGasLimit = block.Header.GasLimit;
        }

        if (!HeaderValidator.ValidateHash(block!.Header, out Hash256 actualHash))
        {
            if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, "invalid block hash"));
            return NewPayloadV1Result.Invalid(null, $"Invalid block hash {request.BlockHash} does not match calculated hash {actualHash}.");
        }

        if (validationService.IsOnInvalidChain(block.Hash!, out Hash256? lastValidHash, block.ParentHash!))
        {
            if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, "block is on an invalid chain"));
            return NewPayloadV1Result.Invalid(lastValidHash, $"Block {request} is on an invalid chain.");
        }

        // validationService will keep track of the block
        return await validationService.ValidateAsync(block) switch
        {
            PayloadStatus.Valid => NewPayloadV1Result.Valid(block.Hash),
            PayloadStatus.Invalid => NewPayloadV1Result.Invalid(null, "Verification failed."),
            PayloadStatus.Syncing => NewPayloadV1Result.Syncing,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private string GetGasChange(long blockGasLimit)
    {
        return (blockGasLimit - _lastBlockGasLimit) switch
        {
            > 0 => "👆",
            < 0 => "👇",
            _ => "  "
        };
    }
}
