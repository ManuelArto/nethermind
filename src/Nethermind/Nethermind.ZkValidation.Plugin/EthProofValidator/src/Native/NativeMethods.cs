// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Runtime.InteropServices;

namespace Nethermind.ZkValidation.Plugin.EthProofValidator.Native;

internal static partial class NativeMethods
{
    private const string LibName = "native_zk_verifier";

    // Standard P/Invoke: .NET automatically handles the translation of 'byte[]' to a pointer
    [LibraryImport(LibName)]
    public static partial int verify(
        int zk_type,
        [In] byte[] proof_ptr, // Pins automatically for the duration of call
        nuint proof_len,
        IntPtr vk_ptr,
        nuint vk_len
    );
}
