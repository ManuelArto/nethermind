// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Runtime.InteropServices;

namespace Nethermind.Merge.Plugin.ZkValidation.EthProofValidator.Native;

internal static class NativeMethods
{
    private const string LibName = "native_zk_verifier";

    // Standard P/Invoke: .NET automatically handles the translation of 'byte[]' to a pointer
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int verify(
        int zkType,
        [In] byte[] proofPtr, // Pins automatically for the duration of call
        nuint proofLen,
        IntPtr vkPtr,
        nuint vkLen
    );
}
