// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.ZkValidation.Plugin;

public class ZkValidationConfig : IZkValidationConfig
{
    public bool Enabled { get; set; }
    public string? ProverUrl { get; set; }
}
