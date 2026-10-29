using System.Security.Cryptography;
using System.Text.Json;
using PocketPC.Core.Security;

namespace PocketPC.Core.Devices;

/// <summary>
/// The list of paired phones, persisted as JSON. Every phone request is checked against this
/// list, so removing a device here blocks it on its very next request.
/// </summary>
public sealed class DeviceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan LastSeenSaveInterval = TimeSpan.FromMinutes(1);

    private readonly string _path;
    private readonly object _lock = new();
    private readonly List<PairedDevice> _devices;

    public DeviceStore(string path)
    {
        _path = path;
        _devices = Load(path);
    }

    /// <summary>Raised (on any thread) after a device is added, removed or updated.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<PairedDevice> All
    {
        get
        {
            lock (_lock)
            {
                return _devices.OrderBy(d => d.PairedAt).ToList();
            }
        }
    }

    public static string NewDeviceId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public PairedDevice? FindByFingerprint(string fingerprint)
    {
        lock (_lock)
        {
            return _devices.FirstOrDefault(d => Fingerprint.Matches(d.CertificateFingerprint, fingerprint));
        }
    }

    public PairedDevice? FindById(string id)
    {
        lock (_lock)
        {
            return _devices.FirstOrDefault(d => d.Id == id);
        }
    }

    /// <summary>Adds a device. Re-pairing the same phone (same certificate) replaces its old entry.</summary>
    public void Add(PairedDevice device)
    {
        lock (_lock)
        {
            _devices.RemoveAll(d => Fingerprint.Matches(d.CertificateFingerprint, device.CertificateFingerprint));
            _devices.Add(device);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public PairedDevice? Remove(string id)
    {
        PairedDevice? removed;
        lock (_lock)
        {
            removed = _devices.FirstOrDefault(d => d.Id == id);
            if (removed is null)
            {
                return null;
            }

            _devices.Remove(removed);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public PairedDevice? Update(string id, Func<PairedDevice, PairedDevice> change)
    {
        PairedDevice? updated;
        lock (_lock)
        {
            var index = _devices.FindIndex(d => d.Id == id);
            if (index < 0)
            {
                return null;
            }

            updated = change(_devices[index]) with { Id = id };
            _devices[index] = updated;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    /// <summary>Records activity; only written to disk once a minute per device to avoid churn.</summary>
    public void Touch(string id, string? address, DateTimeOffset now)
    {
        bool notify;
        lock (_lock)
        {
            var index = _devices.FindIndex(d => d.Id == id);
            if (index < 0)
            {
                return;
            }

            var current = _devices[index];
            var saveDue = current.LastSeenAt is null || now - current.LastSeenAt.Value >= LastSeenSaveInterval
                || current.LastAddress != address;
            _devices[index] = current with { LastSeenAt = now, LastAddress = address };
            if (saveDue)
            {
                Save();
            }

            notify = saveDue;
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(new DeviceFile { Devices = _devices }, JsonOptions);
        AtomicFile.WriteAllText(_path, json);
    }

    private static List<PairedDevice> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var file = JsonSerializer.Deserialize<DeviceFile>(File.ReadAllText(path), JsonOptions);
        return file?.Devices ?? [];
    }

    private sealed class DeviceFile
    {
        public int Version { get; set; } = 1;

        public List<PairedDevice> Devices { get; set; } = [];
    }
}
