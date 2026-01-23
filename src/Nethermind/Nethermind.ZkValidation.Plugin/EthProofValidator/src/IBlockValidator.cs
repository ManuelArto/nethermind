// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;

namespace Nethermind.ZkValidation.Plugin.EthProofValidator;

public interface IBlockValidator
{
    Task<string> ValidateBlockAsync(long blockId, int retry = 0);
}
