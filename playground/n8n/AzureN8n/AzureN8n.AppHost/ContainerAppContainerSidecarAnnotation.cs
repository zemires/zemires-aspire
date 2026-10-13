using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Primitives;

public class ContainerAppContainerSidecarAnnotation(IList<Provisionable> provisionableResources, ContainerApp sidecarContainerApp) : IResourceAnnotation
{
    public ContainerApp? SidecarContainerApp { get; } = sidecarContainerApp;
}