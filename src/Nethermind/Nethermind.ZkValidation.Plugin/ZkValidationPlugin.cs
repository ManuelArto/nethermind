// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Autofac;
using Autofac.Core;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Logging;
using Nethermind.Merge.Plugin;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.ZkValidation.Plugin.Handlers;

namespace Nethermind.ZkValidation.Plugin;

public class ZkValidationPlugin(IZkValidationConfig zkConfig, IMergeConfig mergeConfig) : IConsensusWrapperPlugin
{
    public string Name => "ZkValidation";
    public string Description => "ZK proof-based block validation for stateless clients";
    public string Author => "Nethermind";
    public bool Enabled => zkConfig.Enabled && mergeConfig.Enabled;
    public int Priority => PluginPriorities.Shutter + 100;

    private ILogger _logger;

    public Task Init(INethermindApi nethermindApi)
    {
        _logger = nethermindApi.LogManager.GetClassLogger();
        if (_logger.IsInfo) _logger.Info("Initializing ZkValidation plugin.");
        return Task.CompletedTask;
    }

    public Task InitNetworkProtocol() => Task.CompletedTask;

    public Task InitRpcModules() => Task.CompletedTask;

    public IBlockProducer InitBlockProducer(IBlockProducerFactory consensusPlugin)
    {
        return consensusPlugin.InitBlockProducer();
    }

    public IModule? Module => new ZkValidationPluginModule();
}

public class ZkValidationPluginModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder
            .AddSingleton<ZkValidationService>()
            .AddSingleton<IAsyncHandler<ExecutionPayload, PayloadStatusV1>, ZkNewPayloadHandler>()
            .AddSingleton<IForkchoiceUpdatedHandler, ZkForkchoiceUpdatedHandler>()
            ;
    }
}
