// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Runtime.InteropServices;
using Nethermind.Logging;
using Nethermind.ZkValidation.Plugin.EthProofValidator.Models;
using Nethermind.ZkValidation.Plugin.EthProofValidator.Native;

namespace Nethermind.ZkValidation.Plugin.EthProofValidator.Verifiers;

public class ZkProofVerifier : IDisposable
{
    private readonly ILogger _logger;

    private IntPtr _vkPtr;
    private nuint _vkLen;

    private bool _disposed;

    public ZKType ZkType { get; }

    public ZkProofVerifier(ZKType zkType, string? vkBinary, ILogManager logManager)
    {
        ZkType = zkType;
        if (!string.IsNullOrEmpty(vkBinary)) AllocateVkMemory(vkBinary);
        _logger = logManager.GetClassLogger();
    }

    public ZkResult Verify(byte[] proof)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var result = NativeMethods.verify((int)ZkType, proof, (nuint)proof.Length, _vkPtr, _vkLen);
            return result switch
            {
                1 => ZkResult.Valid,
                0 => ZkResult.Invalid,
                _ => ZkResult.Failed
            };
        }
        catch (Exception e)
        {
            if (_logger.IsInfo) _logger.Info(e.Message);
            return ZkResult.Failed;
        }
    }

    private void AllocateVkMemory(string vkBinary)
    {
        byte[] vkBytes = Convert.FromBase64String(vkBinary);
        _vkLen = (nuint)vkBytes.Length;
        // Allocate unmanaged memory and copy the verification key bytes
        _vkPtr = Marshal.AllocHGlobal(vkBytes.Length);
        try
        {
            Marshal.Copy(vkBytes, 0, _vkPtr, vkBytes.Length);
        }
        catch
        {
            Marshal.FreeHGlobal(_vkPtr);
            throw;
        }
    }

    // --- Disposal Pattern ---
    public void Dispose()
    {
        if (_disposed) return;
        ReleaseVerificationKey();
        _disposed = true;
        System.GC.SuppressFinalize(this);
    }

    private void ReleaseVerificationKey()
    {
        if (_vkPtr == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_vkPtr);
        _vkPtr = IntPtr.Zero;
    }

    ~ZkProofVerifier() => ReleaseVerificationKey();
}
