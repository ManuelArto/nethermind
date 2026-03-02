// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Config;

namespace Nethermind.ZkValidation.Plugin;

public interface IZkValidationConfig : IConfig
{
    [ConfigItem(Description = "Whether to enable ZK Validation mode (Stateless Client).", DefaultValue = "false")]
    bool Enabled { get; set; }

    [ConfigItem(Description = "The URL of the ZK prover service.", DefaultValue = "https://ethproofs.org")]
    public string? ProverUrl { get; set; }
}
