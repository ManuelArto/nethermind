// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Caching;
using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Logging;

namespace Nethermind.Merge.Plugin.Handlers
{
    using ValidationCompletion = TaskCompletionSource<(NewPayloadHandler.ValidationResult? validationResult, string? validationMessage)>;

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

        public async Task<(NewPayloadHandler.ValidationResult, string?)> ExecuteAsync(Block block, BlockHeader parent, ProcessingOptions processingOptions)
        {
            NewPayloadHandler.ValidationResult TryCacheResult(NewPayloadHandler.ValidationResult result, string? errorMessage)
            {
                // notice that it is not correct to add information to the cache
                // if we return SYNCING for example, and don't know yet whether
                // the block is valid or invalid because we haven't processed it yet
                if (result == NewPayloadHandler.ValidationResult.Valid || result == NewPayloadHandler.ValidationResult.Invalid) _latestBlocks?.Set(block.GetOrCalculateHash(), (result == NewPayloadHandler.ValidationResult.Valid, errorMessage));
                return result;
            }

            (NewPayloadHandler.ValidationResult? result, string? validationMessage) = (null, null);

            // If duplicate, reuse results
            if (_latestBlocks is not null && _latestBlocks.TryGet(block.Hash!, out (bool valid, string? message) cachedResult))
            {
                (bool isValid, string? message) = cachedResult;
                if (!isValid)
                {
                    if (_logger.IsWarn) _logger.Warn("Invalid block found in latestBlock cache.");
                }

                return (isValid ? NewPayloadHandler.ValidationResult.Valid : NewPayloadHandler.ValidationResult.Invalid, message);
            }

            // Validate
            if (!ValidateWithBlockValidator(block, parent, out validationMessage))
            {
                return (TryCacheResult(NewPayloadHandler.ValidationResult.Invalid, validationMessage), validationMessage);
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
                    AddBlockResult.InvalidBlock => NewPayloadHandler.ValidationResult.Invalid,
                    // if the block is marked as AlreadyKnown by the block tree then it means it has already
                    // been suggested. there are three possibilities, either the block hasn't been processed yet,
                    // the block was processed and returned invalid but this wasn't saved anywhere or the block was
                    // processed and marked as valid.
                    // if marked as processed by the block tree then return VALID, otherwise null so that it's processed a few lines below
                    AddBlockResult.AlreadyKnown => _blockTree.WasProcessed(block.Number, block.Hash!) ? NewPayloadHandler.ValidationResult.Valid : null,
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
                    // we don't know the result of processing the block, either because
                    // it is the first time we add it to the tree or it's AlreadyKnown in
                    // the tree but hasn't yet been processed. if it's the second case
                    // probably the block is already in the processing queue as a result
                    // of a previous newPayload or the block being discovered during syncing
                    // but add it to the processing queue just in case.
                    await _processingQueue.Enqueue(block, processingOptions);
                    (result, validationMessage) = await blockProcessed.Task.TimeoutOn(timeoutTask, cts);
                }
                else
                {
                    // Already known block with known processing result, cancel the timeout task
                    cts.Cancel();
                }
            }
            catch (TimeoutException)
            {
                // we timed out while processing the block, result will be null and we will return SYNCING below, no need to do anything
                if (_logger.IsDebug) _logger.Debug($"Block {block.ToString(Block.Format.FullHashAndNumber)} timed out when processing. Assume Syncing.");
            }

            return (TryCacheResult(result ?? NewPayloadHandler.ValidationResult.Syncing, validationMessage), validationMessage);
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

            NewPayloadHandler.ValidationResult? validationResult = e.ProcessingResult switch
            {
                ProcessingResult.Success => NewPayloadHandler.ValidationResult.Valid,
                ProcessingResult.ProcessingError => NewPayloadHandler.ValidationResult.Invalid,
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

        private bool ValidateWithBlockValidator(Block block, BlockHeader parent, out string? error)
        {
            block.Header.TotalDifficulty ??= parent.TotalDifficulty + block.Difficulty;
            block.Header.IsPostMerge = true; // I think we don't need to set it again here.
            bool isValid = _blockValidator.ValidateSuggestedBlock(block, parent, out error, validateHashes: false);
            if (!isValid && _logger.IsWarn) _logger.Warn($"Block validator rejected the block {block.ToString(Block.Format.FullHashAndNumber)}.");
            return isValid;
        }

        public void Dispose()
        {
            _processingQueue.BlockRemoved -= GetProcessingQueueOnBlockRemoved;
        }
    }
}
