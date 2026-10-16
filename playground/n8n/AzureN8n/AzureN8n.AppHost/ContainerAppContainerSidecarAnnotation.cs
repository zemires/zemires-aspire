using Azure.Provisioning.AppContainers;

public class ContainerAppContainerSidecarAnnotation(ContainerApp sidecarContainerApp) : IResourceAnnotation
{
    public ContainerApp? SidecarContainerApp { get; } = sidecarContainerApp;
}