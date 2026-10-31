using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

/// <summary>
/// MongoDB Atlas persistence for Himo.Api. IDs are stored as canonical GUID strings
/// so old API contracts do not depend on BSON GuidRepresentation settings.
/// </summary>
sealed class MongoStore
{
    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<MUser> _users;
    private readonly IMongoCollection<MConsent> _consents;
    private readonly IMongoCollection<MPushToken> _pushTokens;
    private readonly IMongoCollection<MOtp> _otpCodes;
    private readonly IMongoCollection<MEmailVerification> _emailVerifications;
    private readonly IMongoCollection<MEmailChange> _emailChanges;
    private readonly IMongoCollection<MPasswordReset> _passwordResets;
    private readonly IMongoCollection<MSession> _sessions;
    private readonly IMongoCollection<MConversation> _conversations;
    private readonly IMongoCollection<MParticipant> _participants;
    private readonly IMongoCollection<MUserState> _userStates;
    private readonly IMongoCollection<MBlockedUser> _blockedUsers;
    private readonly IMongoCollection<MReport> _reports;
    private readonly IMongoCollection<MReceipt> _receipts;
    private readonly IMongoCollection<MMessage> _messages;
    private readonly GridFSBucket _files;
    private readonly object _sync = new();
    private bool _indexesReady;

    public MongoStore(IHostEnvironment environment)
    {
        var uri = Environment.GetEnvironmentVariable("MONGODB_URI")
            ?? Environment.GetEnvironmentVariable("MONGO_URI");
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException("MONGODB_URI لم يتم ضبطه في إعدادات الخادم.");
        if (!uri.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase)
            && !uri.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MONGODB_URI ليس رابط MongoDB صالحًا.");

        var mongoUrl = new MongoUrl(uri);
        var databaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE");
        if (string.IsNullOrWhiteSpace(databaseName)) databaseName = mongoUrl.DatabaseName;
        if (string.IsNullOrWhiteSpace(databaseName)) databaseName = "himo";

        var settings = MongoClientSettings.FromConnectionString(uri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(8);
        settings.ConnectTimeout = TimeSpan.FromSeconds(8);
        settings.SocketTimeout = TimeSpan.FromSeconds(30);
        var client = new MongoClient(settings);
        _db = client.GetDatabase(databaseName);
        _users = _db.GetCollection<MUser>("users");
        _consents = _db.GetCollection<MConsent>("userLegalConsents");
        _pushTokens = _db.GetCollection<MPushToken>("pushTokens");
        _otpCodes = _db.GetCollection<MOtp>("otpCodes");
        _emailVerifications = _db.GetCollection<MEmailVerification>("emailVerificationCodes");
        _emailChanges = _db.GetCollection<MEmailChange>("emailChangeVerificationCodes");
        _passwordResets = _db.GetCollection<MPasswordReset>("passwordResetCodes");
        _sessions = _db.GetCollection<MSession>("sessions");
        _conversations = _db.GetCollection<MConversation>("conversations");
        _participants = _db.GetCollection<MParticipant>("conversationParticipants");
        _userStates = _db.GetCollection<MUserState>("conversationUserStates");
        _blockedUsers = _db.GetCollection<MBlockedUser>("blockedUsers");
        _reports = _db.GetCollection<MReport>("conversationReports");
        _receipts = _db.GetCollection<MReceipt>("messageReceipts");
        _messages = _db.GetCollection<MMessage>("messages");
        _files = new GridFSBucket(_db, new GridFSBucketOptions { BucketName = "messageAttachments", ChunkSizeBytes = 1024 * 1024 });
    }

    public (bool Connected, bool SchemaReady, string? Error) GetHealthStatus()
    {
        try
        {
            _db.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            EnsureIndexes();
            return (true, _indexesReady, _indexesReady ? null : "MongoDB index initialization did not complete.");
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (message.Length > 220) message = message[..220];
            return (false, false, message);
        }
    }

    private void EnsureIndexes()
    {
        if (_indexesReady) return;
        lock (_sync)
        {
            if (_indexesReady) return;
            var uniqueEmail = new CreateIndexOptions<MUser>
            {
                Unique = true,
                Name = "ux_users_email_ci",
                Collation = new Collation("en", strength: CollationStrength.Secondary),
                PartialFilterExpression = Builders<MUser>.Filter.Type(x => x.PhoneNumber, BsonType.String)
            };
            _users.Indexes.CreateOne(new CreateIndexModel<MUser>(Builders<MUser>.IndexKeys.Ascending(x => x.PhoneNumber), uniqueEmail));
            _users.Indexes.CreateOne(new CreateIndexModel<MUser>(Builders<MUser>.IndexKeys.Ascending(x => x.Username), new CreateIndexOptions<MUser> { Unique = true, Name = "ux_users_username_ci", Collation = new Collation("en", strength: CollationStrength.Secondary), PartialFilterExpression = Builders<MUser>.Filter.Type(x => x.Username, BsonType.String) }));
            _users.Indexes.CreateOne(new CreateIndexModel<MUser>(Builders<MUser>.IndexKeys.Ascending(x => x.ContactPhoneNumber), new CreateIndexOptions { Name = "ix_users_contact_phone" }));
            _consents.Indexes.CreateOne(new CreateIndexModel<MConsent>(Builders<MConsent>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.AcceptedAt)));
            _pushTokens.Indexes.CreateOne(new CreateIndexModel<MPushToken>(Builders<MPushToken>.IndexKeys.Ascending(x => x.UserId)));
            _emailVerifications.Indexes.CreateOne(new CreateIndexModel<MEmailVerification>(Builders<MEmailVerification>.IndexKeys.Ascending(x => x.ExpiresAt)));
            _passwordResets.Indexes.CreateOne(new CreateIndexModel<MPasswordReset>(Builders<MPasswordReset>.IndexKeys.Ascending(x => x.ExpiresAt)));
            _sessions.Indexes.CreateOne(new CreateIndexModel<MSession>(Builders<MSession>.IndexKeys.Ascending(x => x.UserId)));
            _sessions.Indexes.CreateOne(new CreateIndexModel<MSession>(Builders<MSession>.IndexKeys.Ascending(x => x.ExpiresAt)));
            _participants.Indexes.CreateOne(new CreateIndexModel<MParticipant>(Builders<MParticipant>.IndexKeys.Ascending(x => x.ConversationId).Ascending(x => x.UserId), new CreateIndexOptions { Unique = true }));
            _participants.Indexes.CreateOne(new CreateIndexModel<MParticipant>(Builders<MParticipant>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.JoinedAt)));
            _userStates.Indexes.CreateOne(new CreateIndexModel<MUserState>(Builders<MUserState>.IndexKeys.Ascending(x => x.ConversationId).Ascending(x => x.UserId), new CreateIndexOptions { Unique = true }));
            _blockedUsers.Indexes.CreateOne(new CreateIndexModel<MBlockedUser>(Builders<MBlockedUser>.IndexKeys.Ascending(x => x.BlockerUserId).Ascending(x => x.BlockedUserId), new CreateIndexOptions { Unique = true }));
            _messages.Indexes.CreateOne(new CreateIndexModel<MMessage>(Builders<MMessage>.IndexKeys.Ascending(x => x.ConversationId).Descending(x => x.SentAt).Descending(x => x.Id)));
            _messages.Indexes.CreateOne(new CreateIndexModel<MMessage>(Builders<MMessage>.IndexKeys.Ascending(x => x.ExpiresAt)));
            _messages.Indexes.CreateOne(new CreateIndexModel<MMessage>(Builders<MMessage>.IndexKeys.Ascending(x => x.ConversationId).Ascending(x => x.SenderUserId).Ascending(x => x.ClientMessageId), new CreateIndexOptions<MMessage> { Unique = true, Name = "ux_message_client_id", PartialFilterExpression = Builders<MMessage>.Filter.Type(x => x.ClientMessageId, BsonType.String) }));
            _receipts.Indexes.CreateOne(new CreateIndexModel<MReceipt>(Builders<MReceipt>.IndexKeys.Ascending(x => x.MessageId).Ascending(x => x.RecipientUserId), new CreateIndexOptions { Unique = true }));
            _conversations.Indexes.CreateOne(new CreateIndexModel<MConversation>(Builders<MConversation>.IndexKeys.Ascending(x => x.OwnerUserId).Descending(x => x.UpdatedAt)));
            _indexesReady = true;
        }
    }

    private static string GuidText(Guid id) => id.ToString("D");
    private static string PairId(Guid first, Guid second) => $"{GuidText(first)}:{GuidText(second)}";
    private static DateTimeOffset ParseTime(string? value, DateTimeOffset fallback = default)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : fallback;
    private static string NowText() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string HashOtp(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    private static bool SecureCodeEquals(string stored, string supplied)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(stored), Convert.FromHexString(HashOtp(supplied))); }
        catch { return false; }
    }
    private static FilterDefinition<T> EqualsCi<T>(System.Linq.Expressions.Expression<Func<T, string?>> field, string value)
    {
        // MongoDB.Driver 3.x Regex expects a FieldDefinition, not the expression overload
        // accepted by Eq/Ne. Wrap the expression explicitly to keep the field strongly typed.
        var fieldDefinition = new ExpressionFieldDefinition<T, string?>(field);
        var pattern = new BsonRegularExpression("^" + Regex.Escape(value.Trim()) + "$", "i");
        return Builders<T>.Filter.Regex(fieldDefinition, pattern);
    }

    public object GetProfile(Guid userId)
    {
        EnsureIndexes();
        var user = _users.Find(x => x.Id == GuidText(userId)).FirstOrDefault() ?? throw new InvalidOperationException("المستخدم غير موجود.");
        return new { userId, email = user.PhoneNumber, name = user.Name, status = user.Status, contactPhoneNumber = user.ContactPhoneNumber, phoneDiscoverable = user.PhoneDiscoverable, hasProfilePhoto = user.ProfilePhoto is { Length: > 0 }, username = user.Username, profilePhotoVersion = user.ProfilePhotoVersion };
    }

    public (string Email, string Name) GetAccountEmailAndName(Guid userId)
    {
        EnsureIndexes();
        var user = _users.Find(x => x.Id == GuidText(userId)).FirstOrDefault() ?? throw new InvalidOperationException("المستخدم غير موجود.");
        return (user.PhoneNumber, user.Name);
    }

    public bool TryUpdateUsername(Guid userId, string username, out string? error)
    {
        EnsureIndexes(); error = null;
        lock (_sync)
        {
            var duplicateFilter = EqualsCi<MUser>(x => x.Username, username)
                & Builders<MUser>.Filter.Ne(x => x.Id, GuidText(userId));
            if (_users.Find(duplicateFilter).FirstOrDefault() is not null)
            { error = "اسم المستخدم مستخدم بالفعل. اختر اسمًا آخر."; return false; }
            try
            {
                var result = _users.UpdateOne(x => x.Id == GuidText(userId), Builders<MUser>.Update.Set(x => x.Username, username));
                if (result.MatchedCount == 0) { error = "المستخدم غير موجود."; return false; }
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                error = "اسم المستخدم مستخدم بالفعل. اختر اسمًا آخر.";
                return false;
            }
        }
    }

    public bool TrySetEmailChangeVerification(Guid userId, string newEmail, string code, DateTimeOffset expiresAt, out int retryAfterSeconds)
    {
        EnsureIndexes(); lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var current = _emailChanges.Find(x => x.UserId == GuidText(userId)).FirstOrDefault();
            if (current is not null)
            {
                var elapsed = now - ParseTime(current.LastSentAt);
                if (elapsed < TimeSpan.FromSeconds(60)) { retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((60 - elapsed.TotalSeconds))); return false; }
            }
            _emailChanges.ReplaceOne(x => x.UserId == GuidText(userId), new MEmailChange { UserId = GuidText(userId), NewEmail = newEmail, Code = HashOtp(code), ExpiresAt = expiresAt.ToString("O"), FailedAttempts = 0, LastSentAt = now.ToString("O") }, new ReplaceOptions { IsUpsert = true });
            retryAfterSeconds = 0; return true;
        }
    }

    public void RemoveEmailChangeVerification(Guid userId) { EnsureIndexes(); _emailChanges.DeleteOne(x => x.UserId == GuidText(userId)); }

    public bool TryConfirmEmailChange(Guid userId, string email, string code, out string? error)
    {
        EnsureIndexes(); error = null; lock (_sync)
        {
            var pending = _emailChanges.Find(x => x.UserId == GuidText(userId)).FirstOrDefault();
            if (pending is null) { error = "اطلب رمز تأكيد جديدًا أولًا."; return false; }
            if (!string.Equals(pending.NewEmail, email, StringComparison.OrdinalIgnoreCase) || ParseTime(pending.ExpiresAt) <= DateTimeOffset.UtcNow || pending.FailedAttempts >= 5)
            { _emailChanges.DeleteOne(x => x.UserId == GuidText(userId)); error = "رمز التأكيد منتهي أو لا يطابق البريد المطلوب. اطلب رمزًا جديدًا."; return false; }
            if (!SecureCodeEquals(pending.Code, code))
            { _emailChanges.UpdateOne(x => x.UserId == GuidText(userId), Builders<MEmailChange>.Update.Inc(x => x.FailedAttempts, 1)); error = "رمز التأكيد غير صحيح."; return false; }
            if (_users.Find(EqualsCi<MUser>(x => x.PhoneNumber, email) & Builders<MUser>.Filter.Ne(x => x.Id, GuidText(userId))).FirstOrDefault() is not null)
            { error = "هذا البريد الإلكتروني مستخدم بالفعل في حساب آخر."; return false; }
            var result = _users.UpdateOne(x => x.Id == GuidText(userId), Builders<MUser>.Update.Set(x => x.PhoneNumber, email.ToLowerInvariant()).Set(x => x.EmailVerified, true));
            if (result.MatchedCount == 0) { error = "المستخدم غير موجود."; return false; }
            _emailChanges.DeleteOne(x => x.UserId == GuidText(userId)); return true;
        }
    }

    public bool TryUpdateContactPhone(Guid userId, string? canonicalPhoneDigits, bool phoneDiscoverable, out string? error)
    {
        EnsureIndexes(); error = null;
        if (phoneDiscoverable && string.IsNullOrWhiteSpace(canonicalPhoneDigits)) { error = "أدخل رقم هاتف قبل السماح بالعثور عليك عبر جهات الاتصال."; return false; }
        var phone = string.IsNullOrWhiteSpace(canonicalPhoneDigits) ? null : canonicalPhoneDigits;
        var result = _users.UpdateOne(x => x.Id == GuidText(userId), Builders<MUser>.Update.Set(x => x.ContactPhoneNumber, phone).Set(x => x.PhoneDiscoverable, phoneDiscoverable));
        if (result.MatchedCount == 0) { error = "المستخدم غير موجود."; return false; }
        return true;
    }

    public IReadOnlyList<ContactMatchResponse> FindUsersByPhoneNumbers(IReadOnlySet<string> normalizedPhoneNumbers, Guid currentUserId)
    {
        EnsureIndexes(); if (normalizedPhoneNumbers.Count == 0) return Array.Empty<ContactMatchResponse>();
        var wanted = normalizedPhoneNumbers.Select(NormalizeContactPhoneDigitsForStore).ToHashSet(StringComparer.Ordinal);
        var users = _users.Find(x => x.Id != GuidText(currentUserId) && x.PhoneDiscoverable && x.ContactPhoneNumber != null).ToList();
        return users.Select(u => new { User = u, Digits = NormalizeContactPhoneDigitsForStore(u.ContactPhoneNumber) })
            .Where(x => wanted.Contains(x.Digits)).GroupBy(x => x.User.Id).Select(g => new ContactMatchResponse(Guid.Parse(g.First().User.Id), g.First().User.Name, "+" + g.First().Digits, g.First().User.ProfilePhoto is { Length: > 0 }, g.First().User.ProfilePhotoVersion))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(200).ToList();
    }

    private static string NormalizeContactPhoneDigitsForStore(string? value)
    {
        var digits = new string((value ?? string.Empty).Select(ch => ch switch { >= '0' and <= '9' => ch, >= '٠' and <= '٩' => (char)('0' + ch - '٠'), >= '۰' and <= '۹' => (char)('0' + ch - '۰'), _ => '\0' }).Where(ch => ch != '\0').ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal)) digits = "966" + digits[1..];
        else if (digits.Length == 9 && digits.StartsWith("5", StringComparison.Ordinal)) digits = "966" + digits;
        return digits;
    }

    public (byte[]? Data, string ContentType) GetProfilePhoto(Guid userId)
    {
        EnsureIndexes(); var user = _users.Find(x => x.Id == GuidText(userId)).FirstOrDefault() ?? throw new InvalidOperationException("المستخدم غير موجود.");
        return (user.ProfilePhoto, user.ProfilePhotoContentType ?? "image/jpeg");
    }

    public void UpdateProfilePhoto(Guid userId, byte[] data, string contentType)
    {
        EnsureIndexes(); var result = _users.UpdateOne(x => x.Id == GuidText(userId), Builders<MUser>.Update.Set(x => x.ProfilePhoto, data).Set(x => x.ProfilePhotoContentType, contentType).Inc(x => x.ProfilePhotoVersion, 1));
        if (result.MatchedCount == 0) throw new InvalidOperationException("المستخدم غير موجود.");
    }

    public void DeleteProfilePhoto(Guid userId)
    {
        EnsureIndexes(); var result = _users.UpdateOne(x => x.Id == GuidText(userId), Builders<MUser>.Update.Unset(x => x.ProfilePhoto).Unset(x => x.ProfilePhotoContentType).Inc(x => x.ProfilePhotoVersion, 1));
        if (result.MatchedCount == 0) throw new InvalidOperationException("المستخدم غير موجود.");
    }

    public void DeleteAccount(Guid userId)
    {
        EnsureIndexes(); lock (_sync)
        {
            var uid = GuidText(userId); var user = _users.Find(x => x.Id == uid).FirstOrDefault() ?? throw new InvalidOperationException("المستخدم غير موجود.");
            var ownedConversations = _conversations.Find(x => x.OwnerUserId == uid).ToList().Select(x => x.Id).ToArray();
            var messagesToDelete = _messages.Find(x => x.SenderUserId == uid || ownedConversations.Contains(x.ConversationId)).ToList();
            var messageIdsToDelete = messagesToDelete.Select(m => m.Id).ToArray();
            foreach (var m in messagesToDelete) DeleteGridFile(m);
            _messages.DeleteMany(x => x.SenderUserId == uid || ownedConversations.Contains(x.ConversationId));
            if (messageIdsToDelete.Length > 0) _receipts.DeleteMany(x => messageIdsToDelete.Contains(x.MessageId));
            _conversations.DeleteMany(x => ownedConversations.Contains(x.Id));
            _participants.DeleteMany(x => x.UserId == uid || ownedConversations.Contains(x.ConversationId));
            _userStates.DeleteMany(x => x.UserId == uid || ownedConversations.Contains(x.ConversationId));
            _blockedUsers.DeleteMany(x => x.BlockerUserId == uid || x.BlockedUserId == uid);
            _reports.DeleteMany(x => x.ReporterUserId == uid || ownedConversations.Contains(x.ConversationId));
            _sessions.DeleteMany(x => x.UserId == uid); _pushTokens.DeleteMany(x => x.UserId == uid);
            _otpCodes.DeleteOne(x => x.PhoneNumber == user.PhoneNumber); _emailVerifications.DeleteOne(x => x.Email == user.PhoneNumber);
            _emailChanges.DeleteOne(x => x.UserId == uid); _passwordResets.DeleteOne(x => x.Email == user.PhoneNumber); _consents.DeleteMany(x => x.UserId == uid);
            _users.DeleteOne(x => x.Id == uid);
        }
    }

    public object UpdateProfile(Guid userId, string name, string status)
    {
        EnsureIndexes(); var result = _users.UpdateOne(x => x.Id == GuidText(userId), Builders<MUser>.Update.Set(x => x.Name, name).Set(x => x.Status, status));
        if (result.MatchedCount == 0) throw new InvalidOperationException("المستخدم غير موجود."); return GetProfile(userId);
    }

    public bool EmailExists(string email) { EnsureIndexes(); return _users.Find(EqualsCi<MUser>(x => x.PhoneNumber, email)).FirstOrDefault() is not null; }

    public bool TrySetEmailVerification(string email, string name, string passwordHash, string? contactPhoneNumber, string code, DateTimeOffset expiresAt, DateTimeOffset consentAcceptedAt, string termsVersion, string privacyVersion, out int retryAfterSeconds)
    {
        EnsureIndexes(); lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var expired in _emailVerifications.Find(Builders<MEmailVerification>.Filter.Lte(x => x.ExpiresAt, now.ToString("O"))).ToList()) _emailVerifications.DeleteOne(x => x.Email == expired.Email);
            var existing = _emailVerifications.Find(x => x.Email == email).FirstOrDefault();
            if (existing is not null)
            {
                var elapsed = now - ParseTime(existing.LastSentAt);
                if (elapsed < TimeSpan.FromSeconds(60)) { retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(60 - elapsed.TotalSeconds)); return false; }
            }
            var record = new MEmailVerification { Email = email, Code = HashOtp(code), Name = name, PasswordHash = passwordHash, ExpiresAt = expiresAt.ToString("O"), FailedAttempts = 0, LastSentAt = now.ToString("O"), ContactPhoneNumber = contactPhoneNumber, TermsAcceptedAt = consentAcceptedAt.ToString("O"), TermsVersion = termsVersion, PrivacyAcceptedAt = consentAcceptedAt.ToString("O"), PrivacyVersion = privacyVersion };
            _emailVerifications.ReplaceOne(x => x.Email == email, record, new ReplaceOptions { IsUpsert = true });
            retryAfterSeconds = 0; return true;
        }
    }

    public void RemoveEmailVerification(string email) { EnsureIndexes(); _emailVerifications.DeleteOne(x => x.Email == email); }

    public bool TryTakeEmailVerification(string email, string code, out EmailVerificationPending? pending)
    {
        EnsureIndexes(); lock (_sync)
        {
            var v = _emailVerifications.Find(x => x.Email == email).FirstOrDefault(); pending = null; if (v is null) return false;
            var termsOk = DateTimeOffset.TryParse(v.TermsAcceptedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var termsAt);
            var privacyOk = DateTimeOffset.TryParse(v.PrivacyAcceptedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var privacyAt);
            if (ParseTime(v.ExpiresAt) <= DateTimeOffset.UtcNow || v.FailedAttempts >= 5 || !termsOk || !privacyOk || string.IsNullOrWhiteSpace(v.TermsVersion) || string.IsNullOrWhiteSpace(v.PrivacyVersion))
            { _emailVerifications.DeleteOne(x => x.Email == email); return false; }
            if (!SecureCodeEquals(v.Code, code)) { _emailVerifications.UpdateOne(x => x.Email == email, Builders<MEmailVerification>.Update.Inc(x => x.FailedAttempts, 1)); return false; }
            _emailVerifications.DeleteOne(x => x.Email == email);
            pending = new EmailVerificationPending(email, v.Name, v.PasswordHash, ParseTime(v.ExpiresAt), v.FailedAttempts, v.ContactPhoneNumber, termsAt, v.TermsVersion!, privacyAt, v.PrivacyVersion!);
            return true;
        }
    }

    public bool TrySetPasswordReset(string email, string code, DateTimeOffset expiresAt, out int retryAfterSeconds)
    {
        EnsureIndexes(); lock (_sync)
        {
            var now = DateTimeOffset.UtcNow; foreach (var expired in _passwordResets.Find(Builders<MPasswordReset>.Filter.Lte(x => x.ExpiresAt, now.ToString("O"))).ToList()) _passwordResets.DeleteOne(x => x.Email == expired.Email);
            var current = _passwordResets.Find(x => x.Email == email).FirstOrDefault();
            if (current is not null) { var elapsed = now - ParseTime(current.LastSentAt); if (elapsed < TimeSpan.FromSeconds(60)) { retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(60 - elapsed.TotalSeconds)); return false; } }
            _passwordResets.ReplaceOne(x => x.Email == email, new MPasswordReset { Email = email, Code = HashOtp(code), ExpiresAt = expiresAt.ToString("O"), FailedAttempts = 0, LastSentAt = now.ToString("O") }, new ReplaceOptions { IsUpsert = true });
            retryAfterSeconds = 0; return true;
        }
    }

    public void RemovePasswordReset(string email) { EnsureIndexes(); _passwordResets.DeleteOne(x => x.Email == email); }

    public bool TryResetPassword(string email, string code, string newPasswordHash, out PasswordResetResult? result, out PasswordResetFailure failure)
    {
        EnsureIndexes(); lock (_sync)
        {
            result = null; failure = PasswordResetFailure.InvalidCode; var reset = _passwordResets.Find(x => x.Email == email).FirstOrDefault(); if (reset is null) return false;
            if (ParseTime(reset.ExpiresAt) <= DateTimeOffset.UtcNow) { _passwordResets.DeleteOne(x => x.Email == email); return false; }
            if (reset.FailedAttempts >= 5) { failure = PasswordResetFailure.RateLimited; return false; }
            if (!SecureCodeEquals(reset.Code, code)) { _passwordResets.UpdateOne(x => x.Email == email, Builders<MPasswordReset>.Update.Inc(x => x.FailedAttempts, 1)); return false; }
            var user = _users.Find(EqualsCi<MUser>(x => x.PhoneNumber, email)).FirstOrDefault(); if (user is null) return false;
            _users.UpdateOne(x => x.Id == user.Id, Builders<MUser>.Update.Set(x => x.PasswordHash, newPasswordHash).Set(x => x.EmailVerified, true));
            _sessions.DeleteMany(x => x.UserId == user.Id); _passwordResets.DeleteOne(x => x.Email == email);
            result = new PasswordResetResult(Guid.Parse(user.Id), user.PhoneNumber, user.Name); failure = PasswordResetFailure.None; return true;
        }
    }

    public UserRecord CreateEmailUser(string email, string name, string passwordHash, bool emailVerified, string? contactPhoneNumber, DateTimeOffset termsAcceptedAt, string termsVersion, DateTimeOffset privacyAcceptedAt, string privacyVersion)
    {
        EnsureIndexes(); lock (_sync)
        {
            if (_users.Find(EqualsCi<MUser>(x => x.PhoneNumber, email)).FirstOrDefault() is not null) throw new InvalidOperationException("هذا البريد الإلكتروني مستخدم بالفعل.");
            var created = new UserRecord(Guid.NewGuid(), email.ToLowerInvariant(), name);
            _users.InsertOne(new MUser { Id = GuidText(created.Id), PhoneNumber = email.ToLowerInvariant(), Name = name, Status = "متاح على Himo", CreatedAt = NowText(), PasswordHash = passwordHash, EmailVerified = emailVerified, ContactPhoneNumber = contactPhoneNumber });
            InsertLegalConsent(created.Id, "terms_of_use", termsVersion, termsAcceptedAt);
            InsertLegalConsent(created.Id, "privacy_policy", privacyVersion, privacyAcceptedAt);
            return created;
        }
    }

    private void InsertLegalConsent(Guid userId, string documentType, string documentVersion, DateTimeOffset acceptedAt)
    {
        var record = new MConsent { Id = $"{GuidText(userId)}:{documentType}:{documentVersion}", UserId = GuidText(userId), DocumentType = documentType, DocumentVersion = documentVersion, AcceptedAt = acceptedAt.ToString("O"), AcceptanceSource = "account_registration" };
        _consents.ReplaceOne(x => x.Id == record.Id, record, new ReplaceOptions { IsUpsert = true });
    }

    public UserRecord? AuthenticateEmail(string email, string password)
    {
        EnsureIndexes(); var user = _users.Find(EqualsCi<MUser>(x => x.PhoneNumber, email)).FirstOrDefault();
        if (user?.PasswordHash is null) return null;
        if (!user.EmailVerified) throw new InvalidOperationException("يجب التحقق من البريد الإلكتروني قبل تسجيل الدخول.");
        return PasswordRules.Verify(password, user.PasswordHash) ? new UserRecord(Guid.Parse(user.Id), user.PhoneNumber, user.Name) : null;
    }

    public void CreateSession(string token, Guid userId, DateTimeOffset expiresAt)
    {
        EnsureIndexes(); lock (_sync) { var now = NowText(); _sessions.DeleteMany(Builders<MSession>.Filter.Lte(x => x.ExpiresAt, now)); _sessions.ReplaceOne(x => x.Token == token, new MSession { Token = token, UserId = GuidText(userId), ExpiresAt = expiresAt.ToString("O") }, new ReplaceOptions { IsUpsert = true }); }
    }

    public bool RevokeSession(HttpRequest request)
    {
        EnsureIndexes(); var token = GetBearerToken(request); if (token is null) return false; return _sessions.DeleteOne(x => x.Token == token).DeletedCount > 0;
    }

    public void RevokeAllSessions(Guid userId) { EnsureIndexes(); var uid = GuidText(userId); _sessions.DeleteMany(x => x.UserId == uid); _pushTokens.DeleteMany(x => x.UserId == uid); }

    public bool TryGetSession(HttpRequest request, out SessionRecord? session)
    {
        EnsureIndexes(); session = null; var token = GetBearerToken(request); if (token is null) return false;
        var stored = _sessions.Find(x => x.Token == token).FirstOrDefault(); if (stored is null) return false;
        var expires = ParseTime(stored.ExpiresAt); if (expires <= DateTimeOffset.UtcNow) { _sessions.DeleteOne(x => x.Token == token); return false; }
        var user = _users.Find(x => x.Id == stored.UserId).FirstOrDefault(); if (user is null) { _sessions.DeleteOne(x => x.Token == token); return false; }
        session = new SessionRecord(Guid.Parse(user.Id), user.PhoneNumber, user.Name, expires); return true;
    }

    public void SavePushToken(Guid userId, string token)
    {
        EnsureIndexes(); if (string.IsNullOrWhiteSpace(token)) return; lock (_sync) { _pushTokens.ReplaceOne(x => x.Token == token, new MPushToken { Token = token, UserId = GuidText(userId), UpdatedAt = NowText() }, new ReplaceOptions { IsUpsert = true }); }
    }
    public void RemovePushToken(Guid userId, string token) { EnsureIndexes(); _pushTokens.DeleteOne(x => x.UserId == GuidText(userId) && x.Token == token); }
    public int GetPushTokenCount() { EnsureIndexes(); return (int)_pushTokens.CountDocuments(FilterDefinition<MPushToken>.Empty); }
    public void RemovePushToken(string token) { EnsureIndexes(); if (!string.IsNullOrWhiteSpace(token)) _pushTokens.DeleteOne(x => x.Token == token); }
    public IReadOnlyList<string> GetPushTokens(IReadOnlyList<Guid> userIds) { EnsureIndexes(); if (userIds.Count == 0) return Array.Empty<string>(); var ids = userIds.Select(GuidText).ToArray(); return _pushTokens.Find(x => ids.Contains(x.UserId)).ToList().Select(x => x.Token).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray(); }
    public IReadOnlyList<Guid> GetPushTokenOwners(IReadOnlyList<string> tokens) { EnsureIndexes(); if (tokens.Count == 0) return Array.Empty<Guid>(); return _pushTokens.Find(x => tokens.Contains(x.Token)).ToList().Select(x => Guid.TryParse(x.UserId, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray(); }

    private static string? GetBearerToken(HttpRequest request)
    {
        var value = request.Headers.TryGetValue("Authorization", out var header) ? header.ToString() : request.Query.TryGetValue("access_token", out var q) ? $"Bearer {q}" : string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = value[7..].Trim(); return token.Length == 64 && token.All(Uri.IsHexDigit) ? token : null;
    }

    public IReadOnlyList<Guid> GetOtherParticipantUserIds(Guid conversationId, Guid currentUserId)
    {
        EnsureIndexes(); var cid = GuidText(conversationId); var uid = GuidText(currentUserId);
        return _participants.Find(x => x.ConversationId == cid && x.UserId != uid).ToList().Select(x => Guid.TryParse(x.UserId, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
    }

    public void ApplyConversationAction(Guid conversationId, Guid userId, string action)
    {
        EnsureIndexes(); action = action?.Trim().ToLowerInvariant() ?? string.Empty;
        if (action is not ("delete" or "archive" or "unarchive" or "mute" or "unmute" or "block" or "unblock")) throw new InvalidOperationException("إجراء المحادثة غير صالح.");
        lock (_sync)
        {
            var cid = GuidText(conversationId); var uid = GuidText(userId);
            if (action is "block" or "unblock")
            {
                var c = _conversations.Find(x => x.Id == cid && !x.IsGroup).FirstOrDefault();
                var members = _participants.Find(x => x.ConversationId == cid).ToList();
                var peer = members.Where(x => x.UserId != uid).ToList();
                if (c is null || members.Count != 2 || !members.Any(x => x.UserId == uid) || peer.Count != 1 || !Guid.TryParse(peer[0].UserId, out var peerGuid))
                    throw new InvalidOperationException("يمكن حظر شخص واحد فقط من محادثة فردية، ولا يمكن حظر مجموعة كاملة.");
                var bid = PairId(userId, peerGuid);
                if (action == "block") _blockedUsers.ReplaceOne(x => x.Id == bid, new MBlockedUser { Id = bid, BlockerUserId = uid, BlockedUserId = GuidText(peerGuid), CreatedAt = NowText() }, new ReplaceOptions { IsUpsert = true });
                else _blockedUsers.DeleteOne(x => x.Id == bid);
                return;
            }
            var state = _userStates.Find(x => x.ConversationId == cid && x.UserId == uid).FirstOrDefault() ?? new MUserState { Id = PairId(conversationId, userId), ConversationId = cid, UserId = uid };
            if (action is "archive" or "unarchive") state.IsArchived = action == "archive";
            if (action is "mute" or "unmute") state.IsMuted = action == "mute";
            if (action == "delete") state.IsDeleted = true;
            state.UpdatedAt = NowText();
            _userStates.ReplaceOne(x => x.Id == state.Id, state, new ReplaceOptions { IsUpsert = true });
        }
    }

    public bool IsConversationBlockedForUser(Guid conversationId, Guid currentUserId)
    {
        EnsureIndexes(); var c = _conversations.Find(x => x.Id == GuidText(conversationId) && !x.IsGroup).FirstOrDefault(); if (c is null) return false;
        var members = _participants.Find(x => x.ConversationId == c.Id).ToList(); if (members.Count != 2 || !members.Any(x => x.UserId == GuidText(currentUserId))) return false;
        var peer = members.First(x => x.UserId != GuidText(currentUserId)).UserId;
        return _blockedUsers.Find(x => (x.BlockerUserId == GuidText(currentUserId) && x.BlockedUserId == peer) || (x.BlockerUserId == peer && x.BlockedUserId == GuidText(currentUserId))).FirstOrDefault() is not null;
    }

    public IReadOnlyList<Guid> GetUnmutedNotificationRecipients(Guid conversationId, IReadOnlyList<Guid> candidateUserIds)
    {
        EnsureIndexes(); if (candidateUserIds.Count == 0) return Array.Empty<Guid>(); var cid = GuidText(conversationId); var ids = candidateUserIds.Select(GuidText).ToArray();
        var muted = _userStates.Find(x => x.ConversationId == cid && ids.Contains(x.UserId) && x.IsMuted).ToList().Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);
        return _participants.Find(x => x.ConversationId == cid && ids.Contains(x.UserId) && !muted.Contains(x.UserId)).ToList().Select(x => Guid.TryParse(x.UserId, out var g) ? g : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
    }

    public IReadOnlyList<ConversationDto> GetConversations(Guid userId)
    {
        EnsureIndexes(); var uid = GuidText(userId); var now = DateTimeOffset.UtcNow;
        var memberships = _participants.Find(x => x.UserId == uid).ToList(); var result = new List<ConversationDto>();
        foreach (var p in memberships)
        {
            var conversation = _conversations.Find(x => x.Id == p.ConversationId).FirstOrDefault(); if (conversation is null) continue;
            var state = _userStates.Find(x => x.ConversationId == p.ConversationId && x.UserId == uid).FirstOrDefault(); if (state?.IsDeleted == true) continue;
            var members = _participants.Find(x => x.ConversationId == p.ConversationId).ToList();
            var isDirect = !conversation.IsGroup && members.Count == 2;
            MUser? other = null;
            if (isDirect)
            {
                var otherId = members.FirstOrDefault(x => x.UserId != uid)?.UserId;
                if (otherId is not null) other = _users.Find(x => x.Id == otherId).FirstOrDefault();
            }
            var visibleMessageFilter = Builders<MMessage>.Filter.Eq(x => x.ConversationId, p.ConversationId)
                & (Builders<MMessage>.Filter.Eq(x => x.ExpiresAt, null) | Builders<MMessage>.Filter.Gt(x => x.ExpiresAt, now.ToString("O", CultureInfo.InvariantCulture)));
            if (p.ClearedBefore is not null)
                visibleMessageFilter &= Builders<MMessage>.Filter.Gt(x => x.SentAt, p.ClearedBefore);
            var last = _messages.Find(visibleMessageFilter)
                .Sort(Builders<MMessage>.Sort.Descending(x => x.SentAt).Descending(x => x.Id))
                .Limit(1).FirstOrDefault();
            var lastReadAt = p.LastReadAt ?? p.JoinedAt;
            var unreadFilter = visibleMessageFilter
                & Builders<MMessage>.Filter.Ne(x => x.SenderUserId, uid)
                & Builders<MMessage>.Filter.Gt(x => x.SentAt, lastReadAt);
            var unread = (int)_messages.CountDocuments(unreadFilter);
            var isBlocked = isDirect && other is not null && _blockedUsers.Find(x => (x.BlockerUserId == uid && x.BlockedUserId == other.Id) || (x.BlockerUserId == other.Id && x.BlockedUserId == uid)).FirstOrDefault() is not null;
            Guid? otherIdParsed = other is not null && Guid.TryParse(other.Id, out var oid) ? oid : null;
            result.Add(new ConversationDto(Guid.Parse(conversation.Id), isDirect && other is not null ? other.Name : conversation.Name, last?.Text ?? string.Empty, ParseTime(conversation.UpdatedAt, now), unread, otherIdParsed, other?.ProfilePhoto is { Length: > 0 }, other?.ProfilePhotoVersion ?? 0L, state?.IsArchived ?? false, state?.IsMuted ?? false, isBlocked, conversation.IsGroup));
        }
        return result.OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    public IReadOnlyList<UserSearchDto> SearchUsers(string email, Guid currentUserId)
    {
        EnsureIndexes(); return _users.Find(EqualsCi<MUser>(x => x.PhoneNumber, email) & Builders<MUser>.Filter.Ne(x => x.Id, GuidText(currentUserId))).Limit(20).ToList().Select(x => new UserSearchDto(Guid.Parse(x.Id), x.PhoneNumber, x.Name, x.ProfilePhoto is { Length: > 0 }, x.ProfilePhotoVersion)).ToArray();
    }

    public IReadOnlyList<UserSearchDto> SearchUsersByName(string query, Guid currentUserId)
    {
        EnsureIndexes(); query = query?.Trim() ?? string.Empty; if (query.Length < 3 || query.Length > 254) return Array.Empty<UserSearchDto>();
        var rx = new BsonRegularExpression(Regex.Escape(query), "i");
        return _users.Find(Builders<MUser>.Filter.Ne(x => x.Id, GuidText(currentUserId)) & Builders<MUser>.Filter.Regex(x => x.Name, rx)).Limit(50).ToList().Where(x => !string.IsNullOrWhiteSpace(x.Name)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => new UserSearchDto(Guid.Parse(x.Id), string.Empty, x.Name, x.ProfilePhoto is { Length: > 0 }, x.ProfilePhotoVersion)).ToArray();
    }

    public IReadOnlyList<UserSearchDto> SearchGroupMembers(string? email, string? normalizedPhone, Guid currentUserId)
    {
        EnsureIndexes(); var uid = GuidText(currentUserId);
        if (!string.IsNullOrWhiteSpace(email)) return _users.Find(EqualsCi<MUser>(x => x.PhoneNumber, email.Trim()) & Builders<MUser>.Filter.Ne(x => x.Id, uid)).Limit(10).ToList().Select(x => new UserSearchDto(Guid.Parse(x.Id), x.PhoneNumber, x.Name, x.ProfilePhoto is { Length: > 0 }, x.ProfilePhotoVersion)).ToArray();
        if (string.IsNullOrWhiteSpace(normalizedPhone)) return Array.Empty<UserSearchDto>();
        var digits = new string(normalizedPhone.Where(char.IsDigit).ToArray()); if (digits.Length is < 7 or > 15) return Array.Empty<UserSearchDto>();
        var local = digits.StartsWith("9665", StringComparison.Ordinal) && digits.Length == 12 ? "0" + digits[3..] : digits;
        var national = digits.StartsWith("966", StringComparison.Ordinal) && digits.Length > 10 ? digits[3..] : digits;
        var wanted = new HashSet<string>(new[] { digits, local, national }, StringComparer.Ordinal);
        return _users.Find(x => x.Id != uid && x.PhoneDiscoverable).ToList().Where(x => wanted.Contains(NormalizeContactPhoneDigitsForStore(x.ContactPhoneNumber)) || wanted.Contains(NormalizeContactPhoneDigitsForStore(x.PhoneNumber)))
            .Take(10).Select(x => new UserSearchDto(Guid.Parse(x.Id), wanted.Contains(NormalizeContactPhoneDigitsForStore(x.ContactPhoneNumber)) ? x.ContactPhoneNumber ?? x.PhoneNumber : x.PhoneNumber, x.Name, x.ProfilePhoto is { Length: > 0 }, x.ProfilePhotoVersion)).ToArray();
    }

    private int CountUnread(string conversationId, string userId, DateTimeOffset now)
    {
        var participant = _participants.Find(x => x.ConversationId == conversationId && x.UserId == userId).FirstOrDefault();
        if (participant is null) return 0;
        var since = participant.LastReadAt ?? participant.JoinedAt;
        return _messages.Find(x => x.ConversationId == conversationId).ToList().Count(x => x.SenderUserId != userId
            && string.CompareOrdinal(x.SentAt, since) > 0
            && (x.ExpiresAt is null || string.CompareOrdinal(x.ExpiresAt, now.ToString("O")) > 0)
            && (participant.ClearedBefore is null || string.CompareOrdinal(x.SentAt, participant.ClearedBefore) > 0));
    }

    public ConversationDto CreateConversation(Guid userId, string name, Guid? participantUserId = null)
    {
        EnsureIndexes(); lock (_sync)
        {
            var uid = GuidText(userId); var now = DateTimeOffset.UtcNow; var timestamp = now.ToString("O");
            if (participantUserId.HasValue)
            {
                if (participantUserId.Value == userId) throw new InvalidOperationException("لا يمكن بدء محادثة مع حسابك.");
                var peer = GuidText(participantUserId.Value); if (_users.Find(x => x.Id == peer).FirstOrDefault() is null) throw new InvalidOperationException("المستخدم غير موجود.");
                var directIds = _participants.Find(x => x.UserId == uid).ToList()
                    .Where(x => _participants.Find(y => y.ConversationId == x.ConversationId && y.UserId == peer).FirstOrDefault() is not null)
                    .Select(x => _conversations.Find(c => c.Id == x.ConversationId && !c.IsGroup).FirstOrDefault())
                    .Where(c => c is not null && _participants.CountDocuments(p => p.ConversationId == c!.Id) == 2)
                    .Cast<MConversation>().OrderByDescending(c => c.UpdatedAt, StringComparer.Ordinal).FirstOrDefault();
                if (directIds is not null)
                {
                    var stId = PairId(Guid.Parse(directIds.Id), userId); var st = _userStates.Find(x => x.Id == stId).FirstOrDefault() ?? new MUserState { Id = stId, ConversationId = directIds.Id, UserId = uid };
                    st.IsDeleted = false; st.IsArchived = false; st.UpdatedAt = timestamp; _userStates.ReplaceOne(x => x.Id == stId, st, new ReplaceOptions { IsUpsert = true });
                    var other = _users.Find(x => x.Id == peer).FirstOrDefault(); var unread = CountUnread(directIds.Id, uid, now);
                    return new ConversationDto(Guid.Parse(directIds.Id), other?.Name ?? directIds.Name, directIds.LastMessage, ParseTime(directIds.UpdatedAt, now), unread, participantUserId);
                }
            }
            var conv = new MConversation { Id = GuidText(Guid.NewGuid()), OwnerUserId = uid, Name = name, LastMessage = string.Empty, UpdatedAt = timestamp, IsGroup = false, DisappearingSeconds = 0 };
            _conversations.InsertOne(conv);
            _participants.InsertOne(new MParticipant { Id = PairId(Guid.Parse(conv.Id), userId), ConversationId = conv.Id, UserId = uid, JoinedAt = timestamp, LastReadAt = timestamp });
            if (participantUserId.HasValue)
                _participants.InsertOne(new MParticipant { Id = PairId(Guid.Parse(conv.Id), participantUserId.Value), ConversationId = conv.Id, UserId = GuidText(participantUserId.Value), JoinedAt = timestamp, LastReadAt = timestamp });
            return new ConversationDto(Guid.Parse(conv.Id), name, string.Empty, now, 0, participantUserId);
        }
    }

    public GroupDetailsDto? GetGroupDetails(Guid conversationId, Guid currentUserId)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var uid = GuidText(currentUserId);
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault();
        if (group is null) return null;
        var currentParticipant = _participants.Find(x => x.ConversationId == cid && x.UserId == uid).FirstOrDefault();
        if (currentParticipant is null) return null;

        var inviteCode = EnsureGroupInviteCode(group);
        var participants = _participants.Find(x => x.ConversationId == cid).ToList();
        var members = new List<GroupMemberDto>(participants.Count);
        foreach (var participant in participants)
        {
            if (!Guid.TryParse(participant.UserId, out var memberId)) continue;
            var user = _users.Find(x => x.Id == participant.UserId).FirstOrDefault();
            if (user is null) continue;
            var isAdmin = participant.UserId == group.OwnerUserId || participant.IsAdmin;
            members.Add(new GroupMemberDto(
                memberId,
                user.Name,
                isAdmin,
                user.ProfilePhoto is { Length: > 0 },
                user.ProfilePhotoVersion,
                ParseTime(participant.JoinedAt, DateTimeOffset.UtcNow),
                user.Status ?? string.Empty,
                user.Username ?? string.Empty));
        }

        var currentCanManage = group.OwnerUserId == uid || currentParticipant.IsAdmin;
        var currentCanShareInvite = currentCanManage || group.AllMembersCanShareInviteLink;
        return new GroupDetailsDto(
            conversationId,
            group.Name,
            group.Description ?? string.Empty,
            currentCanShareInvite ? inviteCode : string.Empty,
            group.GroupPhoto is { Length: > 0 },
            group.GroupPhotoVersion,
            currentCanManage,
            currentCanShareInvite,
            group.DisappearingSeconds,
            members.OrderByDescending(x => x.IsAdmin).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private string EnsureGroupInviteCode(MConversation group)
    {
        if (!string.IsNullOrWhiteSpace(group.InviteCode)) return group.InviteCode;
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var result = _conversations.UpdateOne(
            x => x.Id == group.Id && (x.InviteCode == null || x.InviteCode == string.Empty),
            Builders<MConversation>.Update.Set(x => x.InviteCode, code));
        var refreshed = _conversations.Find(x => x.Id == group.Id).FirstOrDefault();
        return refreshed?.InviteCode ?? code;
    }

    public string? GetGroupInviteCode(Guid conversationId, Guid currentUserId)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault();
        var participant = _participants.Find(x => x.ConversationId == cid && x.UserId == GuidText(currentUserId)).FirstOrDefault();
        if (group is null || participant is null) return null;
        if (!group.AllMembersCanShareInviteLink && !IsGroupAdmin(group, GuidText(currentUserId)))
            throw new UnauthorizedAccessException("مشاركة رابط الدعوة متاحة للمشرفين فقط.");
        return EnsureGroupInviteCode(group);
    }

    public bool UpdateGroupInfo(Guid conversationId, Guid actorUserId, string name, string description)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var actor = GuidText(actorUserId);
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault();
        if (group is null) return false;
        if (!IsGroupAdmin(group, actor)) throw new UnauthorizedAccessException("تعديل بيانات المجموعة متاح للمشرفين فقط.");
        name = (name ?? string.Empty).Trim();
        description = (description ?? string.Empty).Trim();
        if (name.Length is < 2 or > 60) throw new InvalidOperationException("اسم المجموعة يجب أن يتراوح بين حرفين و60 حرفًا.");
        if (description.Length > 500) throw new InvalidOperationException("وصف المجموعة طويل جدًا.");
        _conversations.UpdateOne(x => x.Id == cid, Builders<MConversation>.Update
            .Set(x => x.Name, name)
            .Set(x => x.Description, description)
            .Set(x => x.UpdatedAt, NowText()));
        return true;
    }

    public (byte[]? Data, string ContentType, long Version)? GetGroupPhoto(Guid conversationId, Guid currentUserId)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        if (_participants.Find(x => x.ConversationId == cid && x.UserId == GuidText(currentUserId)).FirstOrDefault() is null)
            return null;
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault();
        if (group is null) return null;
        return (group.GroupPhoto, group.GroupPhotoContentType ?? "image/jpeg", group.GroupPhotoVersion);
    }

    public void UpdateGroupPhoto(Guid conversationId, Guid actorUserId, byte[] data, string contentType)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var actor = GuidText(actorUserId);
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault()
            ?? throw new InvalidOperationException("المجموعة غير موجودة.");
        if (!IsGroupAdmin(group, actor)) throw new UnauthorizedAccessException("تغيير صورة المجموعة متاح للمشرفين فقط.");
        _conversations.UpdateOne(x => x.Id == cid, Builders<MConversation>.Update
            .Set(x => x.GroupPhoto, data)
            .Set(x => x.GroupPhotoContentType, contentType)
            .Inc(x => x.GroupPhotoVersion, 1));
    }

    public void DeleteGroupPhoto(Guid conversationId, Guid actorUserId)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var actor = GuidText(actorUserId);
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault()
            ?? throw new InvalidOperationException("المجموعة غير موجودة.");
        if (!IsGroupAdmin(group, actor)) throw new UnauthorizedAccessException("تغيير صورة المجموعة متاح للمشرفين فقط.");
        _conversations.UpdateOne(x => x.Id == cid, Builders<MConversation>.Update
            .Unset(x => x.GroupPhoto)
            .Unset(x => x.GroupPhotoContentType)
            .Inc(x => x.GroupPhotoVersion, 1));
    }

    public bool PromoteGroupMember(Guid conversationId, Guid actorUserId, Guid memberUserId, bool makeAdmin)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var actor = GuidText(actorUserId);
        var target = GuidText(memberUserId);
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault();
        if (group is null) return false;
        if (!IsGroupAdmin(group, actor)) throw new UnauthorizedAccessException("إدارة الأعضاء متاحة للمشرفين فقط.");
        if (_participants.Find(x => x.ConversationId == cid && x.UserId == target).FirstOrDefault() is null)
            throw new InvalidOperationException("هذا المستخدم ليس عضوًا في المجموعة.");
        if (target == group.OwnerUserId && !makeAdmin)
            throw new InvalidOperationException("لا يمكن إزالة صلاحية مالك المجموعة.");
        _participants.UpdateOne(x => x.ConversationId == cid && x.UserId == target,
            Builders<MParticipant>.Update.Set(x => x.IsAdmin, makeAdmin));
        return true;
    }

    public bool RemoveGroupMember(Guid conversationId, Guid actorUserId, Guid memberUserId)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var actor = GuidText(actorUserId);
        var target = GuidText(memberUserId);
        var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault();
        if (group is null) return false;
        if (!IsGroupAdmin(group, actor)) throw new UnauthorizedAccessException("إزالة الأعضاء متاحة للمشرفين فقط.");
        if (target == group.OwnerUserId) throw new InvalidOperationException("لا يمكن إزالة مالك المجموعة.");
        var result = _participants.DeleteOne(x => x.ConversationId == cid && x.UserId == target);
        return result.DeletedCount > 0;
    }

    public bool LeaveGroup(Guid conversationId, Guid userId)
    {
        EnsureIndexes();
        var cid = GuidText(conversationId);
        var uid = GuidText(userId);

        lock (_sync)
        {
            var group = _conversations.Find(x => x.Id == cid && x.IsGroup).FirstOrDefault();
            if (group is null) return false;

            var current = _participants.Find(x => x.ConversationId == cid && x.UserId == uid).FirstOrDefault();
            if (current is null) return false;

            if (string.Equals(group.OwnerUserId, uid, StringComparison.OrdinalIgnoreCase))
            {
                var remaining = _participants.Find(x => x.ConversationId == cid && x.UserId != uid)
                    .ToList()
                    .OrderByDescending(x => x.IsAdmin)
                    .ThenBy(x => x.JoinedAt, StringComparer.Ordinal)
                    .ToList();

                if (remaining.Count == 0)
                    throw new InvalidOperationException("أنت آخر عضو في المجموعة. أضف عضوًا آخر قبل الخروج حتى لا تبقى المجموعة بلا مشرف.");

                var nextOwner = remaining[0];
                var ownerUpdate = _conversations.UpdateOne(
                    x => x.Id == cid && x.OwnerUserId == uid,
                    Builders<MConversation>.Update.Set(x => x.OwnerUserId, nextOwner.UserId).Set(x => x.UpdatedAt, NowText()));
                if (ownerUpdate.MatchedCount == 0)
                    throw new InvalidOperationException("تعذر نقل ملكية المجموعة. حاول مرة أخرى.");

                if (!nextOwner.IsAdmin)
                {
                    _participants.UpdateOne(
                        x => x.ConversationId == cid && x.UserId == nextOwner.UserId,
                        Builders<MParticipant>.Update.Set(x => x.IsAdmin, true));
                }
            }

            var result = _participants.DeleteOne(x => x.ConversationId == cid && x.UserId == uid);
            return result.DeletedCount > 0;
        }
    }

    public ConversationDto? JoinGroupByInvite(string inviteCode, Guid userId)
    {
        EnsureIndexes();
        inviteCode = (inviteCode ?? string.Empty).Trim();
        if (inviteCode.Length is < 16 or > 80) return null;
        var group = _conversations.Find(x => x.IsGroup && x.InviteCode == inviteCode).FirstOrDefault();
        if (group is null) return null;
        if (group.RequireAdminApproval)
            throw new UnauthorizedAccessException("تتطلب هذه المجموعة موافقة المشرف قبل الانضمام.");
        var uid = GuidText(userId);
        if (_participants.Find(x => x.ConversationId == group.Id && x.UserId == uid).FirstOrDefault() is null)
        {
            var timestamp = NowText();
            _participants.InsertOne(new MParticipant { Id = PairId(Guid.Parse(group.Id), userId), ConversationId = group.Id, UserId = uid, JoinedAt = timestamp, LastReadAt = timestamp });
        }
        return new ConversationDto(Guid.Parse(group.Id), group.Name, group.LastMessage, ParseTime(group.UpdatedAt, DateTimeOffset.UtcNow), 0, isGroup: true);
    }

    private bool IsGroupAdmin(MConversation group, string userId)
    {
        if (group.OwnerUserId == userId) return true;
        return _participants.Find(x => x.ConversationId == group.Id && x.UserId == userId && x.IsAdmin).FirstOrDefault() is not null;
    }

    public ConversationDto CreateGroup(
        Guid ownerUserId, string name, IReadOnlyCollection<Guid> memberUserIds, int disappearingSeconds = 0,
        bool allMembersCanAddMembers = false, bool allMembersCanShareInviteLink = false,
        bool requireAdminApproval = false, bool adminsOnlyMessages = false)
    {
        EnsureIndexes(); var members = memberUserIds.Where(id => id != Guid.Empty && id != ownerUserId).Distinct().ToArray();
        if (disappearingSeconds is not (0 or 3600 or 7200 or 86400 or 432000 or 2592000)) throw new InvalidOperationException("قيمة مؤقت الاختفاء غير مدعومة.");
        if (members.Length == 0) throw new InvalidOperationException("أضف عضوًا واحدًا على الأقل قبل إنشاء المجموعة.");
        if (members.Length > 100) throw new InvalidOperationException("يمكن إضافة 100 عضو كحد أقصى إلى المجموعة.");
        lock (_sync)
        {
            var wanted = members.Select(GuidText).ToArray(); var actual = _users.CountDocuments(x => wanted.Contains(x.Id));
            if (actual != members.Length) throw new InvalidOperationException("أحد الأعضاء المحددين غير موجود أو لم يعد متاحًا.");
            var now = DateTimeOffset.UtcNow; var timestamp = now.ToString("O"); var group = new MConversation
            {
                Id = GuidText(Guid.NewGuid()), OwnerUserId = GuidText(ownerUserId), Name = name.Trim(),
                LastMessage = string.Empty, UpdatedAt = timestamp, IsGroup = true,
                DisappearingSeconds = disappearingSeconds, AllMembersCanAddMembers = allMembersCanAddMembers,
                AllMembersCanShareInviteLink = allMembersCanShareInviteLink, RequireAdminApproval = requireAdminApproval,
                AdminsOnlyMessages = adminsOnlyMessages
            };
            _conversations.InsertOne(group);
            foreach (var id in new[] { ownerUserId }.Concat(members)) _participants.InsertOne(new MParticipant { Id = PairId(Guid.Parse(group.Id), id), ConversationId = group.Id, UserId = GuidText(id), JoinedAt = timestamp, LastReadAt = timestamp, IsAdmin = id == ownerUserId });
            return new ConversationDto(Guid.Parse(group.Id), group.Name, string.Empty, now, 0, isGroup: true);
        }
    }

    public int AddGroupMembers(Guid conversationId, Guid requesterUserId, IReadOnlyCollection<Guid> memberUserIds)
    {
        EnsureIndexes();
        var group = _conversations.Find(x => x.Id == GuidText(conversationId) && x.IsGroup).FirstOrDefault();
        if (group is null) throw new InvalidOperationException("المجموعة غير موجودة.");
        var requester = GuidText(requesterUserId);
        if (_participants.Find(x => x.ConversationId == group.Id && x.UserId == requester).FirstOrDefault() is null)
            throw new InvalidOperationException("أنت لست عضوًا في هذه المجموعة.");
        if (!group.AllMembersCanAddMembers && !IsGroupAdmin(group, requester))
            throw new UnauthorizedAccessException("إضافة الأعضاء متاحة للمشرف فقط في هذه المجموعة.");
        var members = (memberUserIds ?? Array.Empty<Guid>()).Where(x => x != Guid.Empty && x != requesterUserId).Distinct().ToArray();
        if (members.Length == 0) throw new InvalidOperationException("اختر عضوًا واحدًا على الأقل.");
        if (members.Length > 100) throw new InvalidOperationException("يمكن إضافة 100 عضو كحد أقصى في كل طلب.");
        var wanted = members.Select(GuidText).ToArray();
        var found = _users.CountDocuments(x => wanted.Contains(x.Id));
        if (found != members.Length) throw new InvalidOperationException("أحد الحسابات المحددة غير موجود أو لم يعد متاحًا.");
        var timestamp = NowText();
        var added = 0;
        foreach (var memberId in members)
        {
            var uid = GuidText(memberId);
            if (_participants.Find(x => x.ConversationId == group.Id && x.UserId == uid).FirstOrDefault() is not null) continue;
            _participants.InsertOne(new MParticipant { Id = PairId(conversationId, memberId), ConversationId = group.Id, UserId = uid, JoinedAt = timestamp, LastReadAt = timestamp });
            added++;
        }
        return added;
    }

    public bool CanSendGroupMessage(Guid conversationId, Guid userId)
    {
        EnsureIndexes();
        var group = _conversations.Find(x => x.Id == GuidText(conversationId)).FirstOrDefault();
        if (group is null || !group.IsGroup || !group.AdminsOnlyMessages) return true;
        return IsGroupAdmin(group, GuidText(userId));
    }

    public void ReportConversation(Guid conversationId, Guid userId, string reason)
    {
        EnsureIndexes(); if (!ExistsConversation(conversationId, userId)) throw new InvalidOperationException("المحادثة غير موجودة.");
        var id = PairId(conversationId, userId); _reports.ReplaceOne(x => x.Id == id, new MReport { Id = id, ConversationId = GuidText(conversationId), ReporterUserId = GuidText(userId), Reason = reason, CreatedAt = NowText() }, new ReplaceOptions { IsUpsert = true });
    }

    public (bool Exists, bool IsBlocked, IReadOnlyList<Guid> RecipientUserIds) GetMessageSendContext(Guid conversationId, Guid currentUserId)
    {
        EnsureIndexes(); var exists = _participants.Find(x => x.ConversationId == GuidText(conversationId) && x.UserId == GuidText(currentUserId)).FirstOrDefault() is not null;
        return (exists, IsConversationBlockedForUser(conversationId, currentUserId), GetOtherParticipantUserIds(conversationId, currentUserId));
    }
    public bool ExistsConversation(Guid id, Guid userId) { EnsureIndexes(); return _participants.Find(x => x.ConversationId == GuidText(id) && x.UserId == GuidText(userId)).FirstOrDefault() is not null; }

    public IReadOnlyList<(Guid MessageId, Guid SenderUserId)> MarkConversationRead(Guid conversationId, Guid userId)
    {
        EnsureIndexes(); var cid = GuidText(conversationId); var uid = GuidText(userId); var timestamp = NowText();
        if (!ExistsConversation(conversationId, userId)) return Array.Empty<(Guid, Guid)>();
        var messageDocs = _messages.Find(x => x.ConversationId == cid && x.SenderUserId != uid).ToList(); var result = new List<(Guid, Guid)>();
        foreach (var message in messageDocs)
        {
            var rid = PairId(Guid.Parse(message.Id), userId); var receipt = _receipts.Find(x => x.Id == rid).FirstOrDefault();
            if (receipt is not null && receipt.ReadAt is null && Guid.TryParse(message.Id, out var mid) && Guid.TryParse(message.SenderUserId, out var sid)) result.Add((mid, sid));
            receipt ??= new MReceipt { Id = rid, MessageId = message.Id, RecipientUserId = uid };
            receipt.DeliveredAt ??= timestamp; receipt.ReadAt ??= timestamp; _receipts.ReplaceOne(x => x.Id == rid, receipt, new ReplaceOptions { IsUpsert = true });
        }
        _participants.UpdateOne(x => x.ConversationId == cid && x.UserId == uid, Builders<MParticipant>.Update.Set(x => x.LastReadAt, timestamp));
        RestoreConversationVisibilityForUser(conversationId, userId, DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture)); return result;
    }

    public (Guid? SenderUserId, bool Changed) MarkMessageDelivered(Guid messageId, Guid userId)
    {
        EnsureIndexes();
        var now = NowText();
        var visible = Builders<MMessage>.Filter.Eq(x => x.Id, GuidText(messageId))
            & Builders<MMessage>.Filter.Ne(x => x.SenderUserId, GuidText(userId))
            & (Builders<MMessage>.Filter.Eq(x => x.ExpiresAt, null) | Builders<MMessage>.Filter.Gt(x => x.ExpiresAt, now));
        var message = _messages.Find(visible).FirstOrDefault();
        if (message is null || !ExistsConversation(Guid.Parse(message.ConversationId), userId) || !Guid.TryParse(message.SenderUserId, out var senderId)) return (null, false);
        var rid = PairId(messageId, userId); var receipt = _receipts.Find(x => x.Id == rid).FirstOrDefault() ?? new MReceipt { Id = rid, MessageId = message.Id, RecipientUserId = GuidText(userId) };
        var changed = receipt.DeliveredAt is null; receipt.DeliveredAt ??= NowText(); _receipts.ReplaceOne(x => x.Id == rid, receipt, new ReplaceOptions { IsUpsert = true }); return (senderId, changed);
    }

    public int GetDisappearingSeconds(Guid conversationId) { EnsureIndexes(); return _conversations.Find(x => x.Id == GuidText(conversationId)).FirstOrDefault()?.DisappearingSeconds ?? 0; }
    public void SetDisappearingSeconds(Guid conversationId, Guid userId, int seconds)
    {
        EnsureIndexes(); if (seconds is not (0 or 3600 or 7200 or 86400 or 432000 or 2592000)) throw new ArgumentOutOfRangeException(nameof(seconds));
        var cid = GuidText(conversationId); var uid = GuidText(userId);
        if (_participants.Find(p => p.ConversationId == cid && p.UserId == uid).FirstOrDefault() is null)
            throw new InvalidOperationException("لم يتم العثور على المحادثة أو لا تملك صلاحية تعديلها.");
        var result = _conversations.UpdateOne(x => x.Id == cid, Builders<MConversation>.Update.Set(x => x.DisappearingSeconds, seconds));
        if (result.MatchedCount == 0) throw new InvalidOperationException("لم يتم العثور على المحادثة أو لا تملك صلاحية تعديلها.");
    }
    public void ClearConversationForUser(Guid conversationId, Guid userId)
    {
        EnsureIndexes(); var timestamp = NowText(); var result = _participants.UpdateOne(x => x.ConversationId == GuidText(conversationId) && x.UserId == GuidText(userId), Builders<MParticipant>.Update.Set(x => x.ClearedBefore, timestamp).Set(x => x.LastReadAt, timestamp));
        if (result.MatchedCount == 0) throw new InvalidOperationException("لم يتم العثور على عضويتك في المحادثة.");
    }

    private DateTimeOffset? GetMessageExpiry(Guid conversationId, DateTimeOffset sentAt)
    { var seconds = GetDisappearingSeconds(conversationId); return seconds > 0 ? sentAt.AddSeconds(seconds) : null; }

    public int DeleteExpiredMessages()
    {
        EnsureIndexes(); var now = NowText(); var expired = _messages.Find(Builders<MMessage>.Filter.And(Builders<MMessage>.Filter.Ne(x => x.ExpiresAt, null), Builders<MMessage>.Filter.Lte(x => x.ExpiresAt, now))).ToList(); if (expired.Count == 0) return 0;
        var touched = expired.Select(x => x.ConversationId).Distinct().ToArray(); foreach (var m in expired) DeleteGridFile(m);
        var expiredIds = expired.Select(m => m.Id).ToArray();
        _messages.DeleteMany(x => expiredIds.Contains(x.Id));
        _receipts.DeleteMany(x => expiredIds.Contains(x.MessageId));
        foreach (var cid in touched)
        {
            var latest = _messages.Find(x => x.ConversationId == cid).ToList().OrderByDescending(x => x.SentAt, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (latest is not null) _conversations.UpdateOne(x => x.Id == cid, Builders<MConversation>.Update.Set(x => x.LastMessage, latest.Text).Set(x => x.UpdatedAt, latest.SentAt));
            else _conversations.UpdateOne(x => x.Id == cid, Builders<MConversation>.Update.Set(x => x.LastMessage, string.Empty));
        }
        return expired.Count;
    }

    public IReadOnlyList<MessageDto> GetMessages(Guid id, DateTimeOffset? since = null, DateTimeOffset? before = null, Guid? currentUserId = null, Guid? beforeId = null, int limit = 1000)
    {
        EnsureIndexes();
        var cid = GuidText(id);
        var now = NowText();
        var participant = currentUserId.HasValue
            ? _participants.Find(x => x.ConversationId == cid && x.UserId == GuidText(currentUserId.Value)).FirstOrDefault()
            : null;

        var filters = new List<FilterDefinition<MMessage>>
        {
            Builders<MMessage>.Filter.Eq(x => x.ConversationId, cid),
            Builders<MMessage>.Filter.Eq(x => x.ExpiresAt, null) | Builders<MMessage>.Filter.Gt(x => x.ExpiresAt, now)
        };
        if (participant?.ClearedBefore is not null)
            filters.Add(Builders<MMessage>.Filter.Gt(x => x.SentAt, participant.ClearedBefore));
        if (since.HasValue)
            filters.Add(Builders<MMessage>.Filter.Gte(x => x.SentAt, since.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        if (before.HasValue)
        {
            var beforeText = before.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            var beforeFilter = Builders<MMessage>.Filter.Lt(x => x.SentAt, beforeText);
            if (beforeId.HasValue)
            {
                beforeFilter |= Builders<MMessage>.Filter.Eq(x => x.SentAt, beforeText)
                    & Builders<MMessage>.Filter.Lt(x => x.Id, GuidText(beforeId.Value));
            }
            filters.Add(beforeFilter);
        }

        limit = Math.Clamp(limit, 1, 1000);
        var selected = _messages.Find(Builders<MMessage>.Filter.And(filters))
            .Sort(Builders<MMessage>.Sort.Descending(x => x.SentAt).Descending(x => x.Id))
            .Limit(limit)
            .ToList();
        var result = new List<MessageDto>(selected.Count);
        foreach (var m in selected)
        {
            if (!Guid.TryParse(m.Id, out var mid) || !Guid.TryParse(m.ConversationId, out var mcid) || !Guid.TryParse(m.SenderUserId, out var sender)) continue;
            var reply = m.ReplyToMessageId is null ? null : _messages.Find(x => x.Id == m.ReplyToMessageId).FirstOrDefault();
            var status = currentUserId.HasValue ? GetReceiptStatus(m, currentUserId.Value) : "sent";
            result.Add(new MessageDto(mid, mcid, sender, m.SenderPhoneNumber, m.Text, ParseTime(m.SentAt), m.AttachmentFileName, m.AttachmentContentType, m.AttachmentSize, status, Guid.TryParse(m.ReplyToMessageId, out var rid) ? rid : null, reply?.Text, m.EditedAt is not null, m.EditedAt, m.DeletedAt is not null, m.ExpiresAt is null ? null : ParseTime(m.ExpiresAt)));
        }
        result.Sort((a, b) => { var t = a.SentAt.CompareTo(b.SentAt); return t != 0 ? t : a.Id.CompareTo(b.Id); });
        return result;
    }

    private string GetReceiptStatus(MMessage message, Guid currentUserId)
    {
        var recipients = _participants.Find(x => x.ConversationId == message.ConversationId && x.UserId != message.SenderUserId).ToList(); if (recipients.Count == 0) return "sent";
        var receipts = recipients.Select(p => _receipts.Find(r => r.MessageId == message.Id && r.RecipientUserId == p.UserId).FirstOrDefault()).Where(r => r is not null).Cast<MReceipt>().ToList();
        if (receipts.Count == 0) return "sent";
        if (receipts.Count == recipients.Count && receipts.All(x => x.ReadAt is not null)) return "read";
        if (receipts.Count == recipients.Count && receipts.All(x => x.DeliveredAt is not null)) return "delivered";
        return "sent";
    }

    public IReadOnlyList<Guid> GetPresenceRecipientUserIds(Guid userId)
    {
        EnsureIndexes(); var cids = _participants.Find(x => x.UserId == GuidText(userId)).ToList().Select(x => x.ConversationId).Distinct().ToArray();
        return _participants.Find(x => cids.Contains(x.ConversationId) && x.UserId != GuidText(userId)).ToList().Select(x => Guid.TryParse(x.UserId, out var g) ? g : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
    }
    public bool MessageBelongsToConversation(Guid messageId, Guid conversationId) { EnsureIndexes(); return _messages.Find(x => x.Id == GuidText(messageId) && x.ConversationId == GuidText(conversationId)).FirstOrDefault() is not null; }

    private void RestoreConversationVisibilityForAllParticipants(Guid conversationId, DateTimeOffset updatedAt)
    {
        var cid = GuidText(conversationId); foreach (var p in _participants.Find(x => x.ConversationId == cid).ToList()) RestoreConversationVisibilityForUser(conversationId, Guid.Parse(p.UserId), updatedAt);
    }
    private void RestoreConversationVisibilityForUser(Guid conversationId, Guid userId, DateTimeOffset updatedAt)
    {
        var id = PairId(conversationId, userId); var state = _userStates.Find(x => x.Id == id).FirstOrDefault() ?? new MUserState { Id = id, ConversationId = GuidText(conversationId), UserId = GuidText(userId) };
        state.IsDeleted = false; state.UpdatedAt = updatedAt.ToString("O"); _userStates.ReplaceOne(x => x.Id == id, state, new ReplaceOptions { IsUpsert = true });
    }
    private static long ToEpochMicroseconds(DateTimeOffset value) => (value.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) / 10L;

    public MessageDto AddMessage(Guid conversationId, Guid userId, string senderPhone, string text, string? clientMessageId, Guid? replyToMessageId = null)
    {
        EnsureIndexes(); if (!ExistsConversation(conversationId, userId)) throw new InvalidOperationException("المحادثة غير موجودة.");
        if (IsConversationBlockedForUser(conversationId, userId)) throw new InvalidOperationException("لا يمكن إرسال الرسائل في هذه المحادثة لأن أحد الطرفين حظر الآخر.");
        lock (_sync)
        {
            var cid = GuidText(conversationId); var uid = GuidText(userId);
            if (!string.IsNullOrWhiteSpace(clientMessageId))
            {
                var duplicate = _messages.Find(x => x.ConversationId == cid && x.SenderUserId == uid && x.ClientMessageId == clientMessageId).FirstOrDefault();
                if (duplicate is not null) return ToMessageDto(duplicate, userId);
            }
            if (replyToMessageId.HasValue && _messages.Find(x => x.Id == GuidText(replyToMessageId.Value) && x.ConversationId == cid).FirstOrDefault() is null) throw new InvalidOperationException("الرسالة المشار إليها غير موجودة في هذه المحادثة.");
            var sentAt = DateTimeOffset.UtcNow; var message = new MMessage { Id = GuidText(Guid.NewGuid()), ConversationId = cid, SenderUserId = uid, SenderPhoneNumber = senderPhone, Text = text, SentAt = sentAt.ToString("O"), ClientMessageId = string.IsNullOrWhiteSpace(clientMessageId) ? null : clientMessageId, ReplyToMessageId = replyToMessageId.HasValue ? GuidText(replyToMessageId.Value) : null, ExpiresAt = GetMessageExpiry(conversationId, sentAt)?.ToString("O") };
            try { _messages.InsertOne(message); }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey && !string.IsNullOrWhiteSpace(clientMessageId))
            { var prior = _messages.Find(x => x.ConversationId == cid && x.SenderUserId == uid && x.ClientMessageId == clientMessageId).FirstOrDefault(); if (prior is not null) return ToMessageDto(prior, userId); throw; }
            CreateReceiptsForMessage(message); _conversations.UpdateOne(x => x.Id == cid, Builders<MConversation>.Update.Set(x => x.LastMessage, text).Set(x => x.UpdatedAt, message.SentAt));
            RestoreConversationVisibilityForAllParticipants(conversationId, sentAt); return ToMessageDto(message, userId);
        }
    }

    private void CreateReceiptsForMessage(MMessage message)
    {
        foreach (var p in _participants.Find(x => x.ConversationId == message.ConversationId && x.UserId != message.SenderUserId).ToList())
        {
            var id = PairId(Guid.Parse(message.Id), Guid.Parse(p.UserId)); _receipts.ReplaceOne(x => x.Id == id, new MReceipt { Id = id, MessageId = message.Id, RecipientUserId = p.UserId }, new ReplaceOptions { IsUpsert = true });
        }
    }
    private MessageDto ToMessageDto(MMessage m, Guid? currentUserId = null)
    {
        var reply = m.ReplyToMessageId is null ? null : _messages.Find(x => x.Id == m.ReplyToMessageId).FirstOrDefault();
        return new MessageDto(Guid.Parse(m.Id), Guid.Parse(m.ConversationId), Guid.Parse(m.SenderUserId), m.SenderPhoneNumber, m.Text, ParseTime(m.SentAt), m.AttachmentFileName, m.AttachmentContentType, m.AttachmentSize, currentUserId.HasValue ? GetReceiptStatus(m, currentUserId.Value) : "sent", Guid.TryParse(m.ReplyToMessageId, out var replyId) ? replyId : null, reply?.Text, m.EditedAt is not null, m.EditedAt, m.DeletedAt is not null, m.ExpiresAt is null ? null : ParseTime(m.ExpiresAt));
    }

    public (MessageDto? Message, bool Changed) EditMessage(Guid messageId, Guid userId, string text)
    {
        EnsureIndexes(); var m = _messages.Find(x => x.Id == GuidText(messageId) && x.SenderUserId == GuidText(userId) && x.DeletedAt == null).FirstOrDefault();
        if (m is null || (m.ExpiresAt is not null && ParseTime(m.ExpiresAt) <= DateTimeOffset.UtcNow)) return (null, false);
        var edited = NowText(); var result = _messages.UpdateOne(x => x.Id == m.Id && x.SenderUserId == GuidText(userId) && x.DeletedAt == null, Builders<MMessage>.Update.Set(x => x.Text, text).Set(x => x.EditedAt, edited)); if (result.ModifiedCount == 0) return (null, false);
        var latest = _messages.Find(x => x.ConversationId == m.ConversationId).ToList().OrderByDescending(x => x.SentAt, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        if (latest?.Id == m.Id) _conversations.UpdateOne(x => x.Id == m.ConversationId, Builders<MConversation>.Update.Set(x => x.LastMessage, text).Set(x => x.UpdatedAt, edited));
        m.Text = text; m.EditedAt = edited; return (ToMessageDto(m, userId), true);
    }

    public MessageDto? DeleteMessage(Guid messageId, Guid userId)
    {
        EnsureIndexes(); var m = _messages.Find(x => x.Id == GuidText(messageId) && x.SenderUserId == GuidText(userId) && x.DeletedAt == null).FirstOrDefault();
        if (m is null) return null;
        var deleted = NowText();
        var update = _messages.UpdateOne(x => x.Id == m.Id && x.SenderUserId == GuidText(userId) && x.DeletedAt == null,
            Builders<MMessage>.Update.Set(x => x.Text, "تم حذف هذه الرسالة").Set(x => x.DeletedAt, deleted).Unset(x => x.EditedAt));
        if (update.ModifiedCount == 0) return null;
        DeleteGridFile(m);
        _messages.UpdateOne(x => x.Id == m.Id, Builders<MMessage>.Update.Unset(x => x.AttachmentData).Unset(x => x.AttachmentGridFsId).Unset(x => x.AudioGridFsId));
        m.Text = "تم حذف هذه الرسالة"; m.DeletedAt = deleted; m.EditedAt = null; return ToMessageDto(m, userId);
    }

    public MessageDto AddAttachmentMessage(Guid messageId, Guid conversationId, Guid userId, string senderPhone, string displayText, string fileName, string contentType, long size, Stream attachmentStream, CancellationToken cancellationToken = default)
    {
        EnsureIndexes(); if (!ExistsConversation(conversationId, userId)) throw new InvalidOperationException("المحادثة غير موجودة."); if (IsConversationBlockedForUser(conversationId, userId)) throw new InvalidOperationException("لا يمكن إرسال مرفقات في هذه المحادثة لأن أحد الطرفين حظر الآخر.");
        var now = DateTimeOffset.UtcNow;
        var id = _files.UploadFromStream(fileName, attachmentStream, new GridFSUploadOptions { Metadata = new BsonDocument { { "messageId", GuidText(messageId) }, { "contentType", contentType } } }, cancellationToken);
        var message = new MMessage { Id = GuidText(messageId), ConversationId = GuidText(conversationId), SenderUserId = GuidText(userId), SenderPhoneNumber = senderPhone, Text = displayText, SentAt = now.ToString("O"), AttachmentFileName = Path.GetFileName(fileName), AttachmentContentType = contentType, AttachmentSize = size, AttachmentGridFsId = id.ToString(), ExpiresAt = GetMessageExpiry(conversationId, now)?.ToString("O") };
        try { _messages.InsertOne(message); } catch { try { _files.Delete(id); } catch { } throw; }
        CreateReceiptsForMessage(message); _conversations.UpdateOne(x => x.Id == message.ConversationId, Builders<MConversation>.Update.Set(x => x.LastMessage, displayText).Set(x => x.UpdatedAt, message.SentAt)); RestoreConversationVisibilityForAllParticipants(conversationId, now); return ToMessageDto(message, userId);
    }

    public AttachmentStreamRecord? OpenAttachment(Guid messageId, Guid userId)
    {
        EnsureIndexes();
        var m = _messages.Find(x => x.Id == GuidText(messageId) && x.AttachmentFileName != null).FirstOrDefault();
        if (m is null || !ExistsConversation(Guid.Parse(m.ConversationId), userId)) return null;
        if (m.DeletedAt is not null || (m.ExpiresAt is not null && ParseTime(m.ExpiresAt) <= DateTimeOffset.UtcNow)) return null;

        var contentType = m.AttachmentContentType ?? "application/octet-stream";
        if (m.AttachmentData is { Length: > 0 } data)
            return new AttachmentStreamRecord(m.AttachmentFileName!, contentType, new MemoryStream(data, writable: false));
        if (!string.IsNullOrWhiteSpace(m.AttachmentGridFsId) && ObjectId.TryParse(m.AttachmentGridFsId, out var oid))
        {
            try { return new AttachmentStreamRecord(m.AttachmentFileName!, contentType, _files.OpenDownloadStream(oid)); }
            catch (GridFSFileNotFoundException) { return null; }
        }
        if (!string.IsNullOrWhiteSpace(m.AudioGridFsId) && ObjectId.TryParse(m.AudioGridFsId, out var audioId))
        {
            try { return new AttachmentStreamRecord(m.AttachmentFileName!, contentType, _files.OpenDownloadStream(audioId)); }
            catch (GridFSFileNotFoundException) { return null; }
        }
        return null;
    }

    public void SaveAttachmentData(Guid messageId, Guid userId, byte[] data)
    {
        EnsureIndexes(); if (data.Length == 0) return; var m = _messages.Find(x => x.Id == GuidText(messageId) && x.AttachmentFileName != null).FirstOrDefault(); if (m is null || !ExistsConversation(Guid.Parse(m.ConversationId), userId)) return;
        var newId = _files.UploadFromBytes(m.AttachmentFileName!, data); var oldId = m.AttachmentGridFsId;
        _messages.UpdateOne(x => x.Id == m.Id, Builders<MMessage>.Update.Set(x => x.AttachmentGridFsId, newId.ToString()).Unset(x => x.AttachmentData));
        if (!string.IsNullOrWhiteSpace(oldId) && ObjectId.TryParse(oldId, out var old)) { try { _files.Delete(old); } catch { } }
    }

    private void DeleteGridFile(MMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.AttachmentGridFsId) && ObjectId.TryParse(message.AttachmentGridFsId, out var id)) { try { _files.Delete(id); } catch { } }
        if (!string.IsNullOrWhiteSpace(message.AudioGridFsId) && ObjectId.TryParse(message.AudioGridFsId, out var audioId)) { try { _files.Delete(audioId); } catch { } }
    }
}

[BsonIgnoreExtraElements] sealed class MUser { [BsonId] public string Id { get; set; } = ""; public string PhoneNumber { get; set; } = ""; public string Name { get; set; } = ""; public string Status { get; set; } = "متاح على Himo"; public string CreatedAt { get; set; } = ""; public string? PasswordHash { get; set; } public bool EmailVerified { get; set; } public byte[]? ProfilePhoto { get; set; } public string? ProfilePhotoContentType { get; set; } public long ProfilePhotoVersion { get; set; } public string? ContactPhoneNumber { get; set; } public bool PhoneDiscoverable { get; set; } public string? Username { get; set; } }
[BsonIgnoreExtraElements] sealed class MConsent { [BsonId] public string Id { get; set; } = ""; public string UserId { get; set; } = ""; public string DocumentType { get; set; } = ""; public string DocumentVersion { get; set; } = ""; public string AcceptedAt { get; set; } = ""; public string AcceptanceSource { get; set; } = "account_registration"; }
[BsonIgnoreExtraElements] sealed class MPushToken { [BsonId] public string Token { get; set; } = ""; public string UserId { get; set; } = ""; public string UpdatedAt { get; set; } = ""; }
[BsonIgnoreExtraElements] sealed class MOtp { [BsonId] public string PhoneNumber { get; set; } = ""; public string Code { get; set; } = ""; public string ExpiresAt { get; set; } = ""; public int FailedAttempts { get; set; } public string? LastSentAt { get; set; } }
[BsonIgnoreExtraElements] sealed class MEmailVerification { [BsonId] public string Email { get; set; } = ""; public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string PasswordHash { get; set; } = ""; public string ExpiresAt { get; set; } = ""; public int FailedAttempts { get; set; } public string? LastSentAt { get; set; } public string? ContactPhoneNumber { get; set; } public string? TermsAcceptedAt { get; set; } public string? TermsVersion { get; set; } public string? PrivacyAcceptedAt { get; set; } public string? PrivacyVersion { get; set; } }
[BsonIgnoreExtraElements] sealed class MEmailChange { [BsonId] public string UserId { get; set; } = ""; public string NewEmail { get; set; } = ""; public string Code { get; set; } = ""; public string ExpiresAt { get; set; } = ""; public int FailedAttempts { get; set; } public string? LastSentAt { get; set; } }
[BsonIgnoreExtraElements] sealed class MPasswordReset { [BsonId] public string Email { get; set; } = ""; public string Code { get; set; } = ""; public string ExpiresAt { get; set; } = ""; public int FailedAttempts { get; set; } public string? LastSentAt { get; set; } }
[BsonIgnoreExtraElements] sealed class MSession { [BsonId] public string Token { get; set; } = ""; public string UserId { get; set; } = ""; public string ExpiresAt { get; set; } = ""; }
[BsonIgnoreExtraElements] sealed class MConversation { [BsonId] public string Id { get; set; } = ""; public string OwnerUserId { get; set; } = ""; public string Name { get; set; } = ""; public string LastMessage { get; set; } = ""; public string UpdatedAt { get; set; } = ""; public bool IsGroup { get; set; } public int DisappearingSeconds { get; set; } public bool AllMembersCanAddMembers { get; set; } public bool AllMembersCanShareInviteLink { get; set; } public bool RequireAdminApproval { get; set; } public bool AdminsOnlyMessages { get; set; } public string? Description { get; set; } public string? InviteCode { get; set; } public byte[]? GroupPhoto { get; set; } public string? GroupPhotoContentType { get; set; } public long GroupPhotoVersion { get; set; } }
[BsonIgnoreExtraElements] sealed class MParticipant { [BsonId] public string Id { get; set; } = ""; public string ConversationId { get; set; } = ""; public string UserId { get; set; } = ""; public string JoinedAt { get; set; } = ""; public string? LastReadAt { get; set; } public string? ClearedBefore { get; set; } public bool IsAdmin { get; set; } }
[BsonIgnoreExtraElements] sealed class MUserState { [BsonId] public string Id { get; set; } = ""; public string ConversationId { get; set; } = ""; public string UserId { get; set; } = ""; public bool IsArchived { get; set; } public bool IsMuted { get; set; } public bool IsDeleted { get; set; } public string UpdatedAt { get; set; } = ""; }
[BsonIgnoreExtraElements] sealed class MBlockedUser { [BsonId] public string Id { get; set; } = ""; public string BlockerUserId { get; set; } = ""; public string BlockedUserId { get; set; } = ""; public string CreatedAt { get; set; } = ""; }
[BsonIgnoreExtraElements] sealed class MReport { [BsonId] public string Id { get; set; } = ""; public string ConversationId { get; set; } = ""; public string ReporterUserId { get; set; } = ""; public string Reason { get; set; } = ""; public string CreatedAt { get; set; } = ""; }
[BsonIgnoreExtraElements] sealed class MReceipt { [BsonId] public string Id { get; set; } = ""; public string MessageId { get; set; } = ""; public string RecipientUserId { get; set; } = ""; public string? DeliveredAt { get; set; } public string? ReadAt { get; set; } }
[BsonIgnoreExtraElements] sealed class MMessage { [BsonId] public string Id { get; set; } = ""; public string ConversationId { get; set; } = ""; public string SenderUserId { get; set; } = ""; public string SenderPhoneNumber { get; set; } = ""; public string Text { get; set; } = ""; public string SentAt { get; set; } = ""; public string? ClientMessageId { get; set; } public string? AttachmentFileName { get; set; } public string? AttachmentContentType { get; set; } public long? AttachmentSize { get; set; } public byte[]? AttachmentData { get; set; } public string? AttachmentGridFsId { get; set; } public string? AudioGridFsId { get; set; } public string? ReplyToMessageId { get; set; } public string? EditedAt { get; set; } public string? DeletedAt { get; set; } public string? ExpiresAt { get; set; } }
