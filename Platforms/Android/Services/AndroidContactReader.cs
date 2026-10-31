#if ANDROID
using global::Android.Content;
using global::Android.Provider;

namespace Himo.Platforms.Android.Services;

/// <summary>
/// Reads the Android address book on a worker thread and always disposes provider cursors.
/// This avoids the cursor leak observed when the app used Contacts.Default.GetAllAsync().
/// </summary>
public static class AndroidContactReader
{
    private const string ContactIdColumn = "contact_id";
    private const string DisplayNameColumn = "display_name";
    private const string DataColumn = "data1";

    public static Task<IReadOnlyList<AndroidContactSnapshot>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<AndroidContactSnapshot>>(
            () => ReadAllCore(cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<AndroidContactSnapshot> ReadAllCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolver = global::Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("Android Contacts provider is unavailable.");
        var contacts = new Dictionary<string, ContactAccumulator>(StringComparer.Ordinal);
        var projection = new[] { ContactIdColumn, DisplayNameColumn, DataColumn };

        ReadRows(
            resolver,
            ContactsContract.CommonDataKinds.Phone.ContentUri!,
            projection,
            contacts,
            isPhone: true,
            cancellationToken);

        ReadRows(
            resolver,
            ContactsContract.CommonDataKinds.Email.ContentUri!,
            projection,
            contacts,
            isPhone: false,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return contacts.Values
            .Select(item => new AndroidContactSnapshot(
                item.Name,
                item.Phones.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                item.Emails.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void ReadRows(
        ContentResolver resolver,
        global::Android.Net.Uri uri,
        string[] projection,
        Dictionary<string, ContactAccumulator> contacts,
        bool isPhone,
        CancellationToken cancellationToken)
    {
        // Query returns a native cursor. The using scope is essential: without it Android
        // reports "A resource failed to call AbstractCursor.close" after repeated navigation.
        using var cursor = resolver.Query(
            uri,
            projection,
            null,
            null,
            $"{DisplayNameColumn} COLLATE LOCALIZED ASC");

        if (cursor is null)
            return;

        var idIndex = cursor.GetColumnIndex(ContactIdColumn);
        var nameIndex = cursor.GetColumnIndex(DisplayNameColumn);
        var dataIndex = cursor.GetColumnIndex(DataColumn);
        if (dataIndex < 0)
            return;

        while (!cancellationToken.IsCancellationRequested && cursor.MoveToNext())
        {
            var value = cursor.IsNull(dataIndex) ? string.Empty : cursor.GetString(dataIndex)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var name = nameIndex >= 0 && !cursor.IsNull(nameIndex)
                ? cursor.GetString(nameIndex)?.Trim() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                name = value;

            var id = idIndex >= 0 && !cursor.IsNull(idIndex)
                ? cursor.GetString(idIndex)
                : null;
            var key = string.IsNullOrWhiteSpace(id) ? name : id!;

            if (!contacts.TryGetValue(key, out var item))
            {
                item = new ContactAccumulator(name);
                contacts[key] = item;
            }
            else if (string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(name))
            {
                item.Name = name;
            }

            if (isPhone)
                item.Phones.Add(value);
            else
                item.Emails.Add(value);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class ContactAccumulator(string name)
    {
        public string Name { get; set; } = name;
        public HashSet<string> Phones { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Emails { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record AndroidContactSnapshot(string Name, string[] PhoneNumbers, string[] EmailAddresses);
#endif
