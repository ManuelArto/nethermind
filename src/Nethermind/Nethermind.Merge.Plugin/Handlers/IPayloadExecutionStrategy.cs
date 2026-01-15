// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading.Tasks;
using Nethermind.Consensus.Processing;
using Nethermind.Core;

namespace Nethermind.Merge.Plugin.Handlers
{
    public interface IPayloadExecutionStrategy : IDisposable
    {
        Task<(NewPayloadHandler.ValidationResult, string?)> ExecuteAsync(Block block, BlockHeader parent, ProcessingOptions options);
    }
}
