using Aspire.Hosting;

namespace Zemires.Aspire.Hosting.N8n.Tests;

public class N8nBuilderExtensionsTests
{
    [Fact]
    public void WithCustomNodes_MountsReadOnlyBelowCustomFolder()
    {
        var builder = DistributedApplication.CreateBuilder();
        var n8n = builder.AddN8n("n8n").WithCustomNodes("../n8n-nodes-acme/dist");

        var mount = Assert.Single(n8n.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("/home/node/.n8n/custom/n8n-nodes-acme", mount.Target);
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
        Assert.True(mount.IsReadOnly);
        Assert.True(Path.IsPathRooted(mount.Source));
    }

    [Fact]
    public void WithCustomNodes_UsesNameAndWaitsForBuild()
    {
        var builder = DistributedApplication.CreateBuilder();
        var build = builder.AddContainer("build", "alpine");
        var n8n = builder.AddN8n("n8n").WithCustomNodes("./nodes/out", name: "my-nodes", buildResource: build);

        Assert.Equal("/home/node/.n8n/custom/my-nodes", n8n.Resource.Annotations.OfType<ContainerMountAnnotation>().Single().Target);
        var wait = Assert.Single(n8n.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource == build.Resource);
        Assert.Equal(WaitType.WaitForCompletion, wait.WaitType);
    }

    [Fact]
    public void WithCustomNodes_WorksOnWorkers()
    {
        var builder = DistributedApplication.CreateBuilder();
        var worker = builder.AddN8n("n8n").AddWorker("worker");

        worker.WithCustomNodes("../n8n-nodes-acme");

        Assert.Equal("/home/node/.n8n/custom/n8n-nodes-acme", worker.Resource.Annotations.OfType<ContainerMountAnnotation>().Single().Target);
    }

    [Theory]
    [InlineData("../n8n-nodes-acme/dist", "n8n-nodes-acme")]
    [InlineData("../n8n-nodes-acme/dist/", "n8n-nodes-acme")]
    [InlineData("C:\\src\\n8n-nodes-acme\\DIST", "n8n-nodes-acme")]
    [InlineData("./build/n8n-nodes-acme", "n8n-nodes-acme")]
    public void GetCustomNodesName_UsesDirectoryOrParentOfDist(string source, string expected)
    {
        Assert.Equal(expected, N8nBuilderExtensions.GetCustomNodesName(source));
    }

    [Fact]
    public void WithCustomNodes_InvalidName_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var n8n = builder.AddN8n("n8n");

        Assert.Throws<ArgumentException>(() => n8n.WithCustomNodes(".."));
        Assert.Throws<ArgumentException>(() => n8n.WithCustomNodes("./nodes", name: "a/b"));
    }

    [Fact]
    public void WithInstanceOwner_SetsEmail()
    {
        var builder = DistributedApplication.CreateBuilder();
        var n8n = builder.AddN8n("n8n").WithInstanceOwner("admin@dev.local", "Admin", "Local");

        Assert.Equal("admin@dev.local", n8n.Resource.InstanceOwnerEmail);
        Assert.NotNull(n8n.Resource.InstanceOwnerPassword);
    }
}
