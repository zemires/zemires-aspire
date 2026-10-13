using Aspire.Hosting.ApplicationModel;

namespace Zemires.Aspire.Hosting.Zitadel;

/// <summary>
/// Holds the initialization outputs declared on a Zitadel resource or project.
/// </summary>
internal sealed class ZitadelOutputsAnnotation : IResourceAnnotation
{
    private readonly Dictionary<string, ZitadelOutputReference> _outputs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ZitadelOutputReference> Outputs => _outputs.Values;

    public static ZitadelOutputReference GetOrAdd(IResource resource, string name, bool isSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!resource.TryGetLastAnnotation(out ZitadelOutputsAnnotation? annotation))
        {
            annotation = new ZitadelOutputsAnnotation();
            resource.Annotations.Add(annotation);
        }

        if (!annotation._outputs.TryGetValue(name, out ZitadelOutputReference? output))
        {
            output = new ZitadelOutputReference(resource, name, isSecret);
            annotation._outputs.Add(name, output);
        }
        else if (isSecret)
        {
            output.IsSecret = true;
        }

        return output;
    }

    public static ZitadelOutputReference? Find(IResource resource, string name) =>
        resource.TryGetLastAnnotation(out ZitadelOutputsAnnotation? annotation) && annotation._outputs.TryGetValue(name, out ZitadelOutputReference? output)
            ? output
            : null;

    public static IReadOnlyCollection<ZitadelOutputReference> GetAll(IResource resource) =>
        resource.TryGetLastAnnotation(out ZitadelOutputsAnnotation? annotation) ? annotation.Outputs : [];
}
