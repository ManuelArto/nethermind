// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading.Tasks;
using Nethermind.Consensus;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Merge.Plugin.Handlers;

namespace Nethermind.Merge.Plugin.Handlers.Strategies
{
    public interface IPayloadExecutionStrategy : IDisposable
    {
        Task<(ValidationResult, string?)> ExecuteAsync(Block block, BlockHeader parent, ProcessingOptions options);
    }
}
