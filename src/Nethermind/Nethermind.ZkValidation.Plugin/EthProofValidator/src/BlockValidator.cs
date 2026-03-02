// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.ZkValidation.Plugin.EthProofValidator.Clients;
using Nethermind.ZkValidation.Plugin.EthProofValidator.Models;
using Nethermind.ZkValidation.Plugin.EthProofValidator.Verifiers;

namespace Nethermind.ZkValidation.Plugin.EthProofValidator;

public class BlockValidator : IBlockValidator
{
    private readonly EthProofsApiClient _apiClient;
    private readonly VerifierRegistry _registry;
    private readonly ILogger _logger;

    public BlockValidator(IZkValidationConfig zkConfig, ILogManager logManager)
    {
        _apiClient = new EthProofsApiClient(zkConfig, logManager);
        _registry = new VerifierRegistry(_apiClient, logManager);
        _logger = logManager.GetClassLogger();
    }

    public async Task<string> ValidateBlockAsync(long blockId)
    {
        List<ProofMetadata>? proofs = await _apiClient.GetProofsForBlockAsync(blockId);
        if (proofs == null || proofs.Count == 0)
        {
            if (_logger.IsDebug) _logger.Debug($"Block #{blockId} - no proofs found.");
            return PayloadStatus.Syncing;
        }

        IEnumerable<Task<ZkResult>> tasks = proofs.Select(async proof =>
        {
            ZkProofVerifier? verifier = _registry.GetVerifier(proof.ClusterId) ?? await _registry.TryAddVerifierAsync(proof);
            return await ProcessProofAsync(proof, verifier);
        });
        ZkResult[] results = await Task.WhenAll(tasks);

        int validCount = 0, totalCount = 0;
        foreach (ZkResult result in results)
        {
            if (result == ZkResult.Valid) validCount++;
            if (result != ZkResult.Failed && result != ZkResult.Skipped) totalCount++;
        }

        if (totalCount < 1) return PayloadStatus.Syncing;

        bool isValid = validCount * 2 >= totalCount;
        if (isValid)
        {
            if (_logger.IsDebug) _logger.Debug($"✅ BLOCK #{blockId} ACCEPTED ({validCount}/{totalCount})");
        }
        else
        {
            if (_logger.IsWarn) _logger.Warn($"❌ BLOCK #{blockId} REJECTED ({validCount}/{totalCount})");
        }

        return isValid ? PayloadStatus.Valid : PayloadStatus.Invalid;
    }

    private async Task<ZkResult> ProcessProofAsync(ProofMetadata proof, ZkProofVerifier? verifier)
    {
        if (verifier is null)
        {
            var zkType = proof.Cluster.ZkvmVersion.ZkVm.Type;
            DisplayProofResult(ZkResult.Skipped, proof.ProofId, zkType, $"No verifier for cluster {proof.ClusterId}");
            return ZkResult.Skipped;
        }

        var proofBytes = await _apiClient.DownloadProofAsync(proof.ProofId);
        if (proofBytes is null)
        {
            DisplayProofResult(ZkResult.Skipped, proof.ProofId, $"{verifier.ZkType}", "Could not download proof");
            return ZkResult.Skipped;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        ZkResult result = verifier.Verify(proofBytes);
        sw.Stop();

        DisplayProofResult(result, proof.ProofId, $"{verifier.ZkType}", $"{sw.ElapsedMilliseconds} ms");
        return result;
    }

    private void DisplayProofResult(ZkResult result, long proofId, string zkType, string info)
    {
        var status = result switch
        {
            ZkResult.Valid => "✅ Valid",
            ZkResult.Invalid => "❌ Invalid",
            ZkResult.Failed => "⛔ Error",
            ZkResult.Skipped => "⚠️  Skipped",
            _ => "❓ Unknown"
        };

        string message = $"   Proof {proofId} - {zkType,-15} : {status} ({info})";
        if (_logger.IsInfo) _logger.Info(message);
    }

}
