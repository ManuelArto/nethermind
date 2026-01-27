// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using FluentAssertions;
using NUnit.Framework;

namespace Nethermind.ZkValidation.Plugin.Test;

[TestFixture]
public class ZkValidationConfigTests
{
    [Test]
    public void Config_ShouldHaveEnabledFalseByDefault()
    {
        // Arrange & Act
        ZkValidationConfig config = new();

        // Assert
        config.Enabled.Should().BeFalse();
    }

    [Test]
    public void Config_CanSetEnabled()
    {
        // Arrange
        ZkValidationConfig config = new();

        // Act
        config.Enabled = true;

        // Assert
        config.Enabled.Should().BeTrue();
    }

    [Test]
    public void Config_ImplementsInterface()
    {
        // Arrange & Act
        ZkValidationConfig config = new();

        // Assert
        config.Should().BeAssignableTo<IZkValidationConfig>();
    }
}
