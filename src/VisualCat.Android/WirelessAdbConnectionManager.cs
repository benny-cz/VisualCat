using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Android.Content;
using Android.OS;
using Android.Security.Keystore;
using IO.Github.Muntashirakon.Adb;
using Java.Security;
using Java.Security.Cert;
using Java.Security.Spec;
using Java.Util.Concurrent;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace VisualCat.Android;

/// <summary>
/// Owns the narrowly-scoped ADB identity VisualCat uses for explicit Wireless debugging capture.
/// </summary>
/// <remarks>
/// LibADB's BouncyCastle TLS implementation needs a normal software RSA key while it performs the
/// pairing handshake, so an AndroidKeyStore RSA key cannot be passed to it directly on every
/// device. VisualCat therefore keeps the ADB RSA identity encrypted at rest in the app's no-backup
/// private directory. The wrapping AES-256 key is non-exportable and is held by Android Keystore.
/// The decrypted RSA private key exists only in process memory while this manager is alive. The
/// service closes and discards the manager after every capture; a later capture decrypts the same
/// persisted identity, so Android's saved pairing remains valid without retaining key material in
/// memory between captures.
/// </remarks>
internal sealed class WirelessAdbConnectionManager : AbsAdbConnectionManager
{
    private const string LogTag = "VisualCat.WirelessAdb";
    private const string WrappingKeyAlias = "visualcat-wireless-adb-wrap-v1";
    private const string IdentityFileName = "wireless-adb-identity-v1.bin";
    private const string IdentityFileMagic = "VCADBID1";
    private const string PairingMarkerFileName = "wireless-adb-paired-v1";
    private const string PairingMarkerMagic = "VCADBPAIRED1";
    private const int IdentityFormatVersion = 1;
    private const int AesGcmTagBits = 128;
    private const int MaximumIdentityPayloadBytes = 64 * 1024;
    private const int RsaKeySizeBits = 2048;

    private readonly Context _context;
    private readonly IPrivateKey _privateKey;
    private readonly Certificate _certificate;

    internal static bool HasSavedIdentity(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var applicationContext = context.ApplicationContext ?? context;
            var directory = ResolveNoBackupDirectory(applicationContext);

            var identityExists = File.Exists(Path.Combine(directory, IdentityFileName));
            var pairingMarkerPath = Path.Combine(directory, PairingMarkerFileName);
            var pairingCompleted = File.Exists(pairingMarkerPath) &&
                string.Equals(
                    File.ReadAllText(pairingMarkerPath, System.Text.Encoding.ASCII),
                    PairingMarkerMagic,
                    StringComparison.Ordinal);
            using var keyStore = KeyStore.GetInstance("AndroidKeyStore")
                ?? throw new InvalidOperationException("AndroidKeyStore is unavailable.");
            keyStore.Load(null);
            var wrappingKeyExists = keyStore.ContainsAlias(WrappingKeyAlias);
            var exists = identityExists && wrappingKeyExists && pairingCompleted;

            if (!exists && (identityExists || wrappingKeyExists || File.Exists(pairingMarkerPath)))
            {
                global::Android.Util.Log.Warn(
                    LogTag,
                    $"Wireless ADB saved-state is incomplete; encryptedIdentityExists={identityExists}, wrappingKeyExists={wrappingKeyExists}, successfulPairingMarker={pairingCompleted}. A new pairing will be offered.");
            }

            global::Android.Util.Log.Info(
                LogTag,
                exists
                    ? "A previously successful Wireless ADB pairing and encrypted identity are available for reconnect."
                    : "No completed Wireless ADB pairing is available; a pairing code will be required.");
            return exists;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                LogTag,
                $"Could not inspect the saved Wireless ADB identity: {exception.GetType().FullName}: {exception.Message}\n{exception}");
            return false;
        }
    }

    internal WirelessAdbConnectionManager(Context context)
    {
        _context = context.ApplicationContext
            ?? throw new ArgumentException("An Android application context is required.", nameof(context));

        global::Android.Util.Log.Info(LogTag, "Initialising Wireless ADB connection manager.");
        (_privateKey, _certificate) = LoadOrCreateIdentity();

        Api = (int)Build.VERSION.SdkInt;
        HostAddress = "127.0.0.1";
        ThrowOnUnauthorised = true;
        SetTimeout(10, TimeUnit.Seconds!);

        global::Android.Util.Log.Info(
            LogTag,
            $"Wireless ADB manager ready for Android API {(int)Build.VERSION.SdkInt}; host is loopback and connection timeout is 10 seconds.");
    }

    protected override IPrivateKey GetPrivateKey() => _privateKey;

    protected override Certificate GetCertificate() => _certificate;

    protected override string GetDeviceName() => "VisualCat";

    /// <summary>
    /// Records the state transition only after Android accepts the pairing code. Merely creating
    /// an encrypted identity is not evidence that Android trusts it.
    /// </summary>
    internal void MarkPairingSucceeded()
    {
        var markerPath = GetPairingMarkerPath();
        var temporaryPath = markerPath + ".tmp";
        var bytes = System.Text.Encoding.ASCII.GetBytes(PairingMarkerMagic);
        TryDeleteFile(temporaryPath, "before atomic successful-pairing marker write");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 512,
                       FileOptions.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, markerPath, overwrite: true);
            global::Android.Util.Log.Info(
                LogTag,
                "Recorded successful Wireless ADB pairing. No pairing code or secret was stored.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            TryDeleteFile(temporaryPath, "after atomic successful-pairing marker write");
        }
    }

    private (IPrivateKey PrivateKey, Certificate Certificate) LoadOrCreateIdentity()
    {
        var identityPath = GetIdentityPath();
        if (File.Exists(identityPath))
        {
            try
            {
                global::Android.Util.Log.Info(
                    LogTag,
                    "Loading the saved Wireless ADB identity. The encrypted identity file stays in VisualCat's no-backup app-private directory and is unwrapped by Android Keystore.");
                return LoadIdentity(identityPath);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error(
                    LogTag,
                    $"The saved Wireless ADB identity could not be loaded and will be replaced: {exception.GetType().FullName}: {exception.Message}\n{exception}");
                ResetIdentityStorage(identityPath);
            }
        }

        global::Android.Util.Log.Info(
            LogTag,
            "No usable saved Wireless ADB identity exists; generating a new RSA identity and protecting it with Android Keystore AES-GCM.");
        TryDeleteFile(GetPairingMarkerPath(), "before creating a new unpaired identity");
        return CreateAndPersistIdentity(identityPath);
    }

    private static (IPrivateKey PrivateKey, Certificate Certificate) CreateAndPersistIdentity(string identityPath)
    {
        byte[]? privateKeyBytes = null;
        byte[]? certificateBytes = null;
        byte[]? plaintext = null;
        byte[]? encryptedPayload = null;

        try
        {
            using var rsa = RSA.Create();
            rsa.KeySize = RsaKeySizeBits;
            var request = new CertificateRequest(
                "CN=VisualCat Wireless ADB",
                rsa,
                HashAlgorithmName.SHA512,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
            using var managedCertificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(20));

            privateKeyBytes = rsa.ExportPkcs8PrivateKey();
            certificateBytes = managedCertificate.Export(X509ContentType.Cert);
            plaintext = SerialiseIdentity(privateKeyBytes, certificateBytes);

            var wrappingKey = GetOrCreateWrappingKey();
            try
            {
                encryptedPayload = EncryptIdentity(wrappingKey, plaintext, out var iv);
                try
                {
                    PersistEncryptedIdentity(identityPath, iv, encryptedPayload);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(iv);
                }
            }
            finally
            {
                wrappingKey.Dispose();
            }

            var javaIdentity = CreateJavaIdentity(privateKeyBytes, certificateBytes);
            global::Android.Util.Log.Info(
                LogTag,
                $"Generated and persisted Wireless ADB RSA-{RsaKeySizeBits} identity. The on-disk private-key payload is AES-GCM encrypted with a non-exportable Android Keystore key; plaintext key material remains only in process memory.");
            return javaIdentity;
        }
        catch
        {
            TryDeleteFile(identityPath, "after identity generation failure");
            throw;
        }
        finally
        {
            if (privateKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(privateKeyBytes);
            }

            if (certificateBytes is not null)
            {
                CryptographicOperations.ZeroMemory(certificateBytes);
            }

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (encryptedPayload is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedPayload);
            }
        }
    }

    private static (IPrivateKey PrivateKey, Certificate Certificate) LoadIdentity(string identityPath)
    {
        var (iv, encryptedPayload) = ReadEncryptedIdentity(identityPath);
        byte[]? plaintext = null;
        byte[]? privateKeyBytes = null;
        byte[]? certificateBytes = null;

        try
        {
            var wrappingKey = GetOrCreateWrappingKey();
            try
            {
                plaintext = DecryptIdentity(wrappingKey, iv, encryptedPayload);
            }
            finally
            {
                wrappingKey.Dispose();
            }

            (privateKeyBytes, certificateBytes) = DeserialiseIdentity(plaintext);
            var javaIdentity = CreateJavaIdentity(privateKeyBytes, certificateBytes);
            global::Android.Util.Log.Info(
                LogTag,
                $"Saved Wireless ADB identity loaded successfully; privateKeyAlgorithm={javaIdentity.PrivateKey.Algorithm ?? "unknown"}, certificateType={javaIdentity.Certificate.Type ?? "unknown"}.");
            return javaIdentity;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(iv);
            CryptographicOperations.ZeroMemory(encryptedPayload);

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (privateKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(privateKeyBytes);
            }

            if (certificateBytes is not null)
            {
                CryptographicOperations.ZeroMemory(certificateBytes);
            }
        }
    }

    private static (IPrivateKey PrivateKey, Certificate Certificate) CreateJavaIdentity(
        byte[] privateKeyBytes,
        byte[] certificateBytes)
    {
        using var keyFactory = KeyFactory.GetInstance("RSA")
            ?? throw new InvalidOperationException("Android RSA KeyFactory is unavailable.");
        using var keySpec = new PKCS8EncodedKeySpec(privateKeyBytes);
        var privateKey = keyFactory.GeneratePrivate(keySpec)
            ?? throw new InvalidOperationException("Android could not import VisualCat's Wireless ADB private key.");

        try
        {
            using var certificateFactory = CertificateFactory.GetInstance("X.509")
                ?? throw new InvalidOperationException("Android X.509 CertificateFactory is unavailable.");
            using var certificateStream = new MemoryStream(certificateBytes, writable: false);
            var certificate = certificateFactory.GenerateCertificate(certificateStream)
                ?? throw new InvalidOperationException("Android could not import VisualCat's Wireless ADB certificate.");

            return (privateKey, certificate);
        }
        catch
        {
            privateKey.Dispose();
            throw;
        }
    }

    private static IKey GetOrCreateWrappingKey()
    {
        using var keyStore = KeyStore.GetInstance("AndroidKeyStore")
            ?? throw new InvalidOperationException("Android Keystore is unavailable.");
        keyStore.Load(null);

        if (!keyStore.ContainsAlias(WrappingKeyAlias))
        {
            global::Android.Util.Log.Info(
                LogTag,
                "Creating non-exportable AES-256 wrapping key in Android Keystore for the saved Wireless ADB identity.");

            using var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")
                ?? throw new InvalidOperationException("Android Keystore AES key generator is unavailable.");
            using var builder = new KeyGenParameterSpec.Builder(
                WrappingKeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
            using var specification = builder
                .SetKeySize(256)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetRandomizedEncryptionRequired(true)
                .SetUserAuthenticationRequired(false)
                .Build();

            keyGenerator.Init(specification);
            using var generatedKey = keyGenerator.GenerateKey()
                ?? throw new InvalidOperationException("Android Keystore did not generate the VisualCat Wireless ADB wrapping key.");
            global::Android.Util.Log.Info(
                LogTag,
                "Created Android Keystore AES-256/GCM wrapping key. The key is non-exportable and does not require user authentication for background-safe app startup.");
            keyStore.Load(null);
        }

        return keyStore.GetKey(WrappingKeyAlias, null)
            ?? throw new InvalidOperationException("Android Keystore did not return VisualCat's Wireless ADB wrapping key.");
    }

    private static byte[] EncryptIdentity(IKey wrappingKey, byte[] plaintext, out byte[] iv)
    {
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("AES/GCM/NoPadding cipher is unavailable.");
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, wrappingKey);

        iv = cipher.GetIV()
            ?? throw new InvalidOperationException("Android did not provide an AES-GCM initialization vector.");
        if (iv.Length < 12 || iv.Length > 32)
        {
            throw new InvalidDataException($"Android returned an unexpected AES-GCM IV length of {iv.Length} bytes.");
        }

        return cipher.DoFinal(plaintext)
            ?? throw new InvalidOperationException("Android returned no encrypted Wireless ADB identity payload.");
    }

    private static byte[] DecryptIdentity(IKey wrappingKey, byte[] iv, byte[] encryptedPayload)
    {
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("AES/GCM/NoPadding cipher is unavailable.");
        using var parameters = new GCMParameterSpec(AesGcmTagBits, iv);
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, wrappingKey, parameters);

        return cipher.DoFinal(encryptedPayload)
            ?? throw new InvalidOperationException("Android returned no decrypted Wireless ADB identity payload.");
    }

    private static byte[] SerialiseIdentity(byte[] privateKeyBytes, byte[] certificateBytes)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(privateKeyBytes.Length);
            writer.Write(privateKeyBytes);
            writer.Write(certificateBytes.Length);
            writer.Write(certificateBytes);
            writer.Flush();
        }

        if (output.Length is <= 0 or > MaximumIdentityPayloadBytes)
        {
            throw new InvalidDataException($"Generated Wireless ADB identity payload has an invalid size of {output.Length} bytes.");
        }

        return output.ToArray();
    }

    private static (byte[] PrivateKey, byte[] Certificate) DeserialiseIdentity(byte[] plaintext)
    {
        if (plaintext.Length is <= 0 or > MaximumIdentityPayloadBytes)
        {
            throw new InvalidDataException($"Saved Wireless ADB identity payload has an invalid size of {plaintext.Length} bytes.");
        }

        using var input = new MemoryStream(plaintext, writable: false);
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: false);

        var privateKeyLength = reader.ReadInt32();
        ValidateComponentLength(privateKeyLength, input.Length - input.Position, "private key");
        var privateKey = reader.ReadBytes(privateKeyLength);
        if (privateKey.Length != privateKeyLength)
        {
            throw new EndOfStreamException("Saved Wireless ADB private key is truncated.");
        }

        try
        {
            var certificateLength = reader.ReadInt32();
            ValidateComponentLength(certificateLength, input.Length - input.Position, "certificate");
            var certificate = reader.ReadBytes(certificateLength);
            if (certificate.Length != certificateLength)
            {
                throw new EndOfStreamException("Saved Wireless ADB certificate is truncated.");
            }

            if (input.Position != input.Length)
            {
                CryptographicOperations.ZeroMemory(certificate);
                throw new InvalidDataException("Saved Wireless ADB identity contains unexpected trailing data.");
            }

            return (privateKey, certificate);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(privateKey);
            throw;
        }
    }

    private static void ValidateComponentLength(int length, long remaining, string componentName)
    {
        if (length <= 0 || length > MaximumIdentityPayloadBytes || length > remaining)
        {
            throw new InvalidDataException(
                $"Saved Wireless ADB {componentName} length is invalid: length={length}, remaining={remaining}.");
        }
    }

    private static void PersistEncryptedIdentity(string identityPath, byte[] iv, byte[] encryptedPayload)
    {
        var directory = Path.GetDirectoryName(identityPath)
            ?? throw new InvalidOperationException("Wireless ADB identity directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var temporaryPath = identityPath + ".tmp";
        TryDeleteFile(temporaryPath, "before atomic identity write");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.None))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(IdentityFileMagic);
                writer.Write(IdentityFormatVersion);
                writer.Write(iv.Length);
                writer.Write(iv);
                writer.Write(encryptedPayload.Length);
                writer.Write(encryptedPayload);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, identityPath, overwrite: true);
            global::Android.Util.Log.Info(
                LogTag,
                $"Persisted encrypted Wireless ADB identity atomically in the no-backup app-private directory; ciphertextBytes={encryptedPayload.Length}, ivBytes={iv.Length}.");
        }
        finally
        {
            TryDeleteFile(temporaryPath, "after atomic identity write");
        }
    }

    private static (byte[] Iv, byte[] EncryptedPayload) ReadEncryptedIdentity(string identityPath)
    {
        using var stream = new FileStream(identityPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);

        var magic = reader.ReadString();
        if (!string.Equals(magic, IdentityFileMagic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Saved Wireless ADB identity has an unknown file signature.");
        }

        var version = reader.ReadInt32();
        if (version != IdentityFormatVersion)
        {
            throw new InvalidDataException($"Saved Wireless ADB identity has unsupported format version {version}.");
        }

        var ivLength = reader.ReadInt32();
        if (ivLength is < 12 or > 32 || ivLength > stream.Length - stream.Position)
        {
            throw new InvalidDataException($"Saved Wireless ADB identity has invalid IV length {ivLength}.");
        }

        var iv = reader.ReadBytes(ivLength);
        if (iv.Length != ivLength)
        {
            throw new EndOfStreamException("Saved Wireless ADB identity IV is truncated.");
        }

        try
        {
            var payloadLength = reader.ReadInt32();
            if (payloadLength <= AesGcmTagBits / 8 ||
                payloadLength > MaximumIdentityPayloadBytes + (AesGcmTagBits / 8) ||
                payloadLength > stream.Length - stream.Position)
            {
                throw new InvalidDataException($"Saved Wireless ADB identity has invalid ciphertext length {payloadLength}.");
            }

            var encryptedPayload = reader.ReadBytes(payloadLength);
            if (encryptedPayload.Length != payloadLength)
            {
                throw new EndOfStreamException("Saved Wireless ADB identity ciphertext is truncated.");
            }

            if (stream.Position != stream.Length)
            {
                CryptographicOperations.ZeroMemory(encryptedPayload);
                throw new InvalidDataException("Saved Wireless ADB identity file contains unexpected trailing data.");
            }

            return (iv, encryptedPayload);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(iv);
            throw;
        }
    }

    private string GetIdentityPath()
    {
        return Path.Combine(ResolveNoBackupDirectory(_context), IdentityFileName);
    }

    private string GetPairingMarkerPath() =>
        Path.Combine(ResolveNoBackupDirectory(_context), PairingMarkerFileName);

    private static string ResolveNoBackupDirectory(Context context)
    {
        var directory = context.NoBackupFilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Android did not provide NoBackupFilesDir for the Wireless ADB identity. VisualCat will not fall back to backup-eligible storage.");
        }

        return directory;
    }

    private static void ResetIdentityStorage(string identityPath)
    {
        TryDeleteFile(identityPath, "while resetting unusable identity");
        var directory = Path.GetDirectoryName(identityPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            TryDeleteFile(
                Path.Combine(directory, PairingMarkerFileName),
                "while resetting the successful-pairing marker for an unusable identity");
        }

        try
        {
            using var keyStore = KeyStore.GetInstance("AndroidKeyStore");
            if (keyStore is null)
            {
                return;
            }

            keyStore.Load(null);
            if (keyStore.ContainsAlias(WrappingKeyAlias))
            {
                keyStore.DeleteEntry(WrappingKeyAlias);
                global::Android.Util.Log.Info(
                    LogTag,
                    "Deleted the unusable Wireless ADB wrapping key from Android Keystore; the next pairing will create a fresh identity.");
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(
                LogTag,
                $"Could not delete the unusable Wireless ADB wrapping key: {exception.GetType().FullName}: {exception.Message}");
        }
    }

    private static void TryDeleteFile(string path, string reason)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                global::Android.Util.Log.Info(LogTag, $"Deleted Wireless ADB state file {reason}.");
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(
                LogTag,
                $"Could not delete Wireless ADB state file {reason}: {exception.GetType().FullName}: {exception.Message}");
        }
    }
}
