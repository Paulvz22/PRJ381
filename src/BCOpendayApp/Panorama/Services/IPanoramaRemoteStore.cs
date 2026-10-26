using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>Online 360° views in Firestore (see <see cref="PanoramaManifest"/> for the layout).</summary>
public interface IPanoramaRemoteStore
{
    /// <summary>False when this build has no Firebase config file or Firebase failed to start.</summary>
    bool IsConfigured { get; }

    /// <summary>All online views (small documents only - no images).</summary>
    Task<IReadOnlyList<PanoramaManifest>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and checks one view's image.</summary>
    Task<byte[]> DownloadAsync(PanoramaManifest manifest, CancellationToken cancellationToken);

    /// <summary>Staff only (Firestore rules): uploads the pieces first, then the manifest, so visitors only ever
    /// see complete views. Returns the manifest that was written.</summary>
    Task<PanoramaManifest> UploadAsync(PanoramaDraft draft, byte[] image, string uid, IProgress<double>? progress);

    /// <summary>Staff only: removes a view for every visitor.</summary>
    Task DeleteAsync(PanoramaManifest manifest);
}
