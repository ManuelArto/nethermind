// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http.Json;
using Nethermind.Logging;
using Nethermind.ZkValidation.Plugin.EthProofValidator.Models;

namespace Nethermind.ZkValidation.Plugin.EthProofValidator.Clients;

public class EthProofsApiClient
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public EthProofsApiClient(IZkValidationConfig zkConfig, ILogManager logManager)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 20
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(zkConfig.ProverUrl ?? "https://ethproofs.org") };
        _logger = logManager.GetClassLogger();
    }

    public async Task<string?> GetVerificationKeyBinaryAsync(long proofId)
    {
        try
        {
            var vkBytes = await _httpClient.GetByteArrayAsync($"/api/verification-keys/download/{proofId}");
            return Convert.ToBase64String(vkBytes);
        }
        catch (HttpRequestException)
        {
            if (_logger.IsDebug) _logger.Debug($"[API Error] Failed to fetch verification key for proof {proofId}");
            return null;
        }
    }

    public async Task<List<ProofMetadata>?> GetProofsForBlockAsync(long blockId)
    {
        try
        {
            ProofResponse? results = await _httpClient.GetFromJsonAsync<ProofResponse>($"/api/blocks/{blockId}/proofs?page_size=20");
            return results?.Rows.Where(p => p.Status == "proved").ToList();
        }
        catch (HttpRequestException ex)
        {
            if (_logger.IsDebug) _logger.Debug($"[API Error] Failed to fetch proofs for block {blockId}: {ex.Message}");
            return null;
        }
    }

    public async Task<byte[]?> DownloadProofAsync(long proofId)
    {
        try
        {
            return await _httpClient.GetByteArrayAsync($"/api/proofs/download/{proofId}");
        }
        catch (HttpRequestException)
        {
            if (_logger.IsDebug) _logger.Debug($"[API Error] Failed to fetch proof {proofId}");
            return null;
        }
    }
}
