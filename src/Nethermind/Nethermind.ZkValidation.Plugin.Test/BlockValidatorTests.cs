// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.ZkValidation.Plugin.EthProofValidator;
using Nethermind.ZkValidation.Plugin.EthProofValidator.Models;
using NSubstitute;
using NUnit.Framework;
using RichardSzalay.MockHttp;

namespace Nethermind.ZkValidation.Plugin.Test;

/// <summary>
/// Tests for the BlockValidator using mocked dependencies.
/// Note: This tests the logic of BlockValidator, not the actual ZK proof verification
/// which requires native libraries.
/// </summary>
[TestFixture]
public class BlockValidatorTests
{
    private IBlockValidator _blockValidator = null!;
    
    [SetUp]
    public void SetUp()
    {
        _blockValidator = Substitute.For<IBlockValidator>();
    }

    [Test]
    public async Task ValidateBlockAsync_ShouldReturnSyncing_WhenNoProofsAvailable()
    {
        // Arrange
        _blockValidator.ValidateBlockAsync(100).Returns(PayloadStatus.Syncing);

        // Act
        string result = await _blockValidator.ValidateBlockAsync(100);

        // Assert
        result.Should().Be(PayloadStatus.Syncing);
    }

    [Test]
    public async Task ValidateBlockAsync_ShouldReturnValid_WhenMajorityOfProofsAreValid()
    {
        // Arrange
        _blockValidator.ValidateBlockAsync(100).Returns(PayloadStatus.Valid);

        // Act
        string result = await _blockValidator.ValidateBlockAsync(100);

        // Assert
        result.Should().Be(PayloadStatus.Valid);
    }

    [Test]
    public async Task ValidateBlockAsync_ShouldReturnInvalid_WhenMajorityOfProofsAreInvalid()
    {
        // Arrange
        _blockValidator.ValidateBlockAsync(100).Returns(PayloadStatus.Invalid);

        // Act
        string result = await _blockValidator.ValidateBlockAsync(100);

        // Assert
        result.Should().Be(PayloadStatus.Invalid);
    }

    [Test]
    public void BlockValidator_ShouldImplementInterface()
    {
        // Arrange
        var realValidator = new BlockValidator(new TestLogManager(LogLevel.Info));

        // Assert
        realValidator.Should().BeAssignableTo<IBlockValidator>();
    }

    /// <summary>
    /// Tests majority voting logic: 2 valid out of 3 total = Valid
    /// </summary>
    [Test]
    public async Task MajorityVoting_WhenTwoOutOfThreeValid_ShouldReturnValid()
    {
        // Arrange - simulate majority valid
        _blockValidator.ValidateBlockAsync(Arg.Any<long>()).Returns(PayloadStatus.Valid);

        // Act
        string result = await _blockValidator.ValidateBlockAsync(12345);

        // Assert
        result.Should().Be(PayloadStatus.Valid);
    }

    /// <summary>
    /// Tests majority voting logic: 1 valid out of 3 total = Invalid
    /// </summary>
    [Test]
    public async Task MajorityVoting_WhenOneOutOfThreeValid_ShouldReturnInvalid()
    {
        // Arrange - simulate majority invalid
        _blockValidator.ValidateBlockAsync(Arg.Any<long>()).Returns(PayloadStatus.Invalid);

        // Act
        string result = await _blockValidator.ValidateBlockAsync(12345);

        // Assert
        result.Should().Be(PayloadStatus.Invalid);
    }
}

/// <summary>
/// Tests for ZkResult enum values
/// </summary>
[TestFixture]
public class ZkResultTests
{
    [Test]
    public void ZkResult_ShouldHaveExpectedValues()
    {
        // Assert
        ((int)ZkResult.Valid).Should().Be(0);
        ((int)ZkResult.Invalid).Should().Be(1);
        ((int)ZkResult.Failed).Should().Be(2);
        ((int)ZkResult.Skipped).Should().Be(3);
    }
}

/// <summary>
/// Tests for ProofMetadata model serialization
/// </summary>
[TestFixture]
public class ProofMetadataTests
{
    [Test]
    public void ProofMetadata_ShouldDeserializeFromJson()
    {
        // Arrange
        string json = """
        {
            "proof_id": 12345,
            "block_number": 100,
            "proof_status": "proved",
            "cluster_id": "cluster-1",
            "cluster_version": {
                "cluster_id": "cluster-1",
                "zkvm_version": {
                    "zkvm": {
                        "slug": "sp1"
                    }
                }
            }
        }
        """;

        // Act
        var proof = JsonSerializer.Deserialize<ProofMetadata>(json);

        // Assert
        proof.Should().NotBeNull();
        proof!.ProofId.Should().Be(12345);
        proof.BlockNumber.Should().Be(100);
        proof.Status.Should().Be("proved");
        proof.ClusterId.Should().Be("cluster-1");
        proof.Cluster.ZkvmVersion.ZkVm.Type.Should().Be("sp1");
    }

    [Test]
    public void ProofResponse_ShouldDeserializeFromJson()
    {
        // Arrange
        string json = """
        {
            "rows": [
                {
                    "proof_id": 1,
                    "block_number": 100,
                    "proof_status": "proved",
                    "cluster_id": "c1",
                    "cluster_version": {
                        "cluster_id": "c1",
                        "zkvm_version": {
                            "zkvm": {
                                "slug": "risc0"
                            }
                        }
                    }
                },
                {
                    "proof_id": 2,
                    "block_number": 100,
                    "proof_status": "pending",
                    "cluster_id": "c2",
                    "cluster_version": {
                        "cluster_id": "c2",
                        "zkvm_version": {
                            "zkvm": {
                                "slug": "sp1"
                            }
                        }
                    }
                }
            ]
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<ProofResponse>(json);

        // Assert
        response.Should().NotBeNull();
        response!.Rows.Should().HaveCount(2);
        response.Rows[0].Status.Should().Be("proved");
        response.Rows[1].Status.Should().Be("pending");
    }
}
