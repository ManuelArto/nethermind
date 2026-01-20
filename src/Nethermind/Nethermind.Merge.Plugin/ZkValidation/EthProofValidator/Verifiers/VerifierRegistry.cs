// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.ZkValidation.EthProofValidator.Clients;
using Nethermind.Merge.Plugin.ZkValidation.EthProofValidator.Models;

namespace Nethermind.Merge.Plugin.ZkValidation.EthProofValidator.Verifiers;

public class VerifierRegistry(EthProofsApiClient apiClient, ILogManager logManager): IDisposable
{
    private readonly ConcurrentDictionary<string, ZkProofVerifier> _verifiers = new();

    public ZkProofVerifier? GetVerifier(string clusterId)
    {
        _verifiers.TryGetValue(clusterId, out var verifier);
        return verifier;
    }

    public async Task<ZkProofVerifier?> TryAddVerifierAsync(ProofMetadata proof)
    {
        var type = proof.Cluster.ZkvmVersion.ZkVm.Type;
        var vkBinary = await apiClient.GetVerificationKeyBinaryAsync(proof.ProofId);

        RegisterVerifier(proof.ClusterId, type, vkBinary);
        return GetVerifier(proof.ClusterId);
    }

    private void RegisterVerifier(string clusterId, string zkVm, string? vkBinary)
    {
        ZKType zkType = ZkTypeMapper.Parse(zkVm);
        if (zkType == ZKType.Unknown) return;

        if (string.IsNullOrEmpty(vkBinary) && !IsVerifiableWithoutVk(zkType)) return;

        _verifiers.AddOrUpdate(clusterId,
            _ => new ZkProofVerifier(zkType, vkBinary, logManager),
            (_, oldVerifier) =>
            {
                oldVerifier.Dispose();
                return new ZkProofVerifier(zkType, vkBinary, logManager);
            });
    }

    public void Dispose()
    {
        foreach (ZkProofVerifier verifier in _verifiers.Values)
        {
            verifier.Dispose();
        }
        _verifiers.Clear();
        System.GC.SuppressFinalize(this);
    }

    // This Verifier(s) handles vk internally
    private static bool IsVerifiableWithoutVk(ZKType zkType) {
        return zkType == ZKType.Airbender;
    }
}
