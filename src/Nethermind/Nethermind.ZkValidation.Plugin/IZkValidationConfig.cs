// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Config;

namespace Nethermind.ZkValidation.Plugin;

public interface IZkValidationConfig : IConfig
{
    [ConfigItem(Description = "Whether to enable ZK Validation mode (Stateless Client).", DefaultValue = "false")]
    bool Enabled { get; set; }
}
