// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using FluentAssertions;
using Nethermind.Merge.Plugin;
using NUnit.Framework;

namespace Nethermind.ZkValidation.Plugin.Test;

[TestFixture]
public class ZkValidationPluginTests
{
    [Test]
    public void Plugin_ShouldBeDisabled_WhenZkConfigDisabled()
    {
        // Arrange
        IZkValidationConfig zkConfig = new ZkValidationConfig { Enabled = false };
        IMergeConfig mergeConfig = new MergeConfig { Enabled = true };

        // Act
        ZkValidationPlugin plugin = new(zkConfig, mergeConfig);

        // Assert
        plugin.Enabled.Should().BeFalse();
    }

    [Test]
    public void Plugin_ShouldBeEnabled_WhenBothConfigsEnabled()
    {
        // Arrange
        IZkValidationConfig zkConfig = new ZkValidationConfig { Enabled = true };
        IMergeConfig mergeConfig = new MergeConfig { Enabled = true };

        // Act
        ZkValidationPlugin plugin = new(zkConfig, mergeConfig);

        // Assert
        plugin.Enabled.Should().BeTrue();
    }

    [Test]
    public void Plugin_ShouldBeDisabled_WhenMergeDisabled()
    {
        // Arrange
        IZkValidationConfig zkConfig = new ZkValidationConfig { Enabled = true };
        IMergeConfig mergeConfig = new MergeConfig { Enabled = false };

        // Act
        ZkValidationPlugin plugin = new(zkConfig, mergeConfig);

        // Assert
        plugin.Enabled.Should().BeFalse();
    }

    [Test]
    public void Plugin_HasCorrectMetadata()
    {
        // Arrange
        IZkValidationConfig zkConfig = new ZkValidationConfig();
        IMergeConfig mergeConfig = new MergeConfig();

        // Act
        ZkValidationPlugin plugin = new(zkConfig, mergeConfig);

        // Assert
        plugin.Name.Should().Be("ZkValidation");
        plugin.Author.Should().Be("Nethermind");
        plugin.Description.Should().Contain("ZK");
    }

    [Test]
    public void Plugin_HasValidPriority()
    {
        // Arrange
        IZkValidationConfig zkConfig = new ZkValidationConfig();
        IMergeConfig mergeConfig = new MergeConfig();

        // Act
        ZkValidationPlugin plugin = new(zkConfig, mergeConfig);

        // Assert - should be after Shutter
        plugin.Priority.Should().BeGreaterThan(Nethermind.Api.PluginPriorities.Shutter);
    }

    [Test]
    public void Plugin_HasModule()
    {
        // Arrange
        IZkValidationConfig zkConfig = new ZkValidationConfig();
        IMergeConfig mergeConfig = new MergeConfig();

        // Act
        ZkValidationPlugin plugin = new(zkConfig, mergeConfig);

        // Assert
        plugin.Module.Should().NotBeNull();
        plugin.Module.Should().BeOfType<ZkValidationPluginModule>();
    }
}
