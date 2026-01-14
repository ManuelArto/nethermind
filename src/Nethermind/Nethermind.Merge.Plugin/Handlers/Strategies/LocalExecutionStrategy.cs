// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Caching;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Threading;
using Nethermind.Crypto;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.Merge.Plugin.Handlers.Strategies
{
    using ValidationCompletion = TaskCompletionSource<(ValidationResult? validationResult, string? validationMessage)>;

    public class LocalExecutionStrategy : IPayloadExecutionStrategy
    {
        private readonly IBlockTree _blockTree;
        private readonly IBlockProcessingQueue _processingQueue;
        private readonly IBlockValidator _blockValidator;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<Hash256, ValidationCompletion> _blockValidationTasks = new();
        private readonly TimeSpan _timeout;
        private readonly LruCache<Hash256AsKey, (bool valid, string? message)>? _latestBlocks;

        public LocalExecutionStrategy(
            IBlockTree blockTree,
            IBlockProcessingQueue processingQueue,
            IBlockValidator blockValidator,
            ILogManager logManager,
            TimeSpan timeout,
            LruCache<Hash256AsKey, (bool valid, string? message)>? latestBlocks)
        {
            _blockTree = blockTree;
            _processingQueue = processingQueue;
            _blockValidator = blockValidator;
            _logger = logManager.GetClassLogger();
            _timeout = timeout;
            _latestBlocks = latestBlocks;
            _processingQueue.BlockRemoved += GetProcessingQueueOnBlockRemoved;
        }

        public async Task<(ValidationResult, string?)> ExecuteAsync(Block block, BlockHeader parent, ProcessingOptions processingOptions)
        {
            ValidationResult TryCacheResult(ValidationResult result, string? errorMessage)
            {
                if (result == ValidationResult.Valid || result == ValidationResult.Invalid)
                    _latestBlocks?.Set(block.GetOrCalculateHash(), (result == ValidationResult.Valid, errorMessage));
                return result;
            }

            (ValidationResult? result, string? validationMessage) = (null, null);

            // If duplicate, reuse results
            if (_latestBlocks is not null && _latestBlocks.TryGet(block.Hash!, out (bool valid, string? message) cachedResult))
            {
                (bool isValid, string? message) = cachedResult;
                if (!isValid)
                {
                    if (_logger.IsWarn) _logger.Warn("Invalid block found in latestBlock cache.");
                }
                return (isValid ? ValidationResult.Valid : ValidationResult.Invalid, message);
            }

            // Validate
            if (!ValidateWithBlockValidator(block, parent, out validationMessage))
            {
                return (TryCacheResult(ValidationResult.Invalid, validationMessage), validationMessage);
            }

            ValidationCompletion blockProcessed =
                _blockValidationTasks.GetOrAdd(
                    block.Hash!,
                    static (k) => new(TaskCreationOptions.RunContinuationsAsynchronously));

            try
            {
                CancellationTokenSource cts = new();
                Task timeoutTask = Task.Delay(_timeout, cts.Token);

                AddBlockResult addResult = await _blockTree
                    .SuggestBlockAsync(block, BlockTreeSuggestOptions.ForceDontSetAsMain)
                    .AsTask().TimeoutOn(timeoutTask);

                result = addResult switch
                {
                    AddBlockResult.InvalidBlock => ValidationResult.Invalid,
                    AddBlockResult.AlreadyKnown => _blockTree.WasProcessed(block.Number, block.Hash!) ? ValidationResult.Valid : null,
                    _ => null
                };

                validationMessage = addResult switch
                {
                    AddBlockResult.InvalidBlock => "Block couldn't be added to the tree.",
                    AddBlockResult.AlreadyKnown => "Block was already known in the tree.",
                    _ => null
                };

                if (!result.HasValue)
                {
                    await _processingQueue.Enqueue(block, processingOptions);
                    (result, validationMessage) = await blockProcessed.Task.TimeoutOn(timeoutTask, cts);
                }
                else
                {
                    cts.Cancel();
                }
            }
            catch (TimeoutException)
            {
                if (_logger.IsDebug) _logger.Debug($"Block {block.ToString(Block.Format.FullHashAndNumber)} timed out when processing. Assume Syncing.");
            }

            return (TryCacheResult(result ?? ValidationResult.Syncing, validationMessage), validationMessage);
        }

        private bool ValidateWithBlockValidator(Block block, BlockHeader parent, out string? errorMessage)
        {
            if (!_blockValidator.ValidateSuggestedBlock(block, parent, out errorMessage))
            {
                if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(block, errorMessage));
                return false;
            }

            return true;
        }

        private void GetProcessingQueueOnBlockRemoved(object? o, BlockRemovedEventArgs e)
        {
            if (!_blockValidationTasks.TryRemove(e.BlockHash, out ValidationCompletion? blockProcessed))
            {
                // If we don't have a task for this block, it means it was already processed or removed.
                return;
            }

            if (e.ProcessingResult == ProcessingResult.Exception)
            {
                BlockchainException? exception = new(e.Exception?.Message ?? "Block processing threw exception.", e.Exception);
                blockProcessed.TrySetException(exception);
                return;
            }

            ValidationResult? validationResult = e.ProcessingResult switch
            {
                ProcessingResult.Success => ValidationResult.Valid,
                ProcessingResult.ProcessingError => ValidationResult.Invalid,
                _ => null
            };

            string? validationMessage = e.ProcessingResult switch
            {
                ProcessingResult.QueueException => "Block cannot be added to processing queue.",
                ProcessingResult.MissingBlock => "Block wasn't found in tree.",
                ProcessingResult.ProcessingError => e.Message ?? "Block processing failed.",
                _ => null
            };

            blockProcessed.TrySetResult((validationResult, validationMessage));
        }

        public void Dispose()
        {
            _processingQueue.BlockRemoved -= GetProcessingQueueOnBlockRemoved;
        }
    }
}
