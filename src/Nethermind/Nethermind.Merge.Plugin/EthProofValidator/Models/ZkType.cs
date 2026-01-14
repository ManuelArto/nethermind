// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;

namespace Nethermind.Merge.Plugin.EthProofValidator.Models;
    public enum ZKType
    {
        Zisk = 0,
        OpenVM = 1,
        Pico = 2,
        Sp1Hypercube = 3,
        Unknown = -1
    }

    public static class ZkTypeMapper
    {
        private static readonly Dictionary<string, ZKType> TypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "zisk", ZKType.Zisk },
            { "openvm", ZKType.OpenVM },
            { "pico", ZKType.Pico },
            { "sp1", ZKType.Sp1Hypercube },
            { "sp1-hypercube", ZKType.Sp1Hypercube },
            { "sp1-turbo", ZKType.Sp1Hypercube },
        };

        public static ZKType Parse(string name)
        {
            return TypeMap.TryGetValue(name, out var type) ? type : ZKType.Unknown;
        }
    }