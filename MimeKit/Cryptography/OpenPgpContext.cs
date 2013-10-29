//
// OpenPgpContext.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2013 Jeffrey Stedfast
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace MimeKit.Cryptography {
	/// <summary>
	/// An OpenPGP cryptography context which can be used for PGP/MIME.
	/// </summary>
	public abstract class OpenPgpContext : CryptographyContext
	{
		/// <summary>
		/// Gets the public key ring path.
		/// </summary>
		/// <value>The public key ring path.</value>
		protected string PublicKeyRingPath {
			get; private set;
		}

		/// <summary>
		/// Gets the secret key ring path.
		/// </summary>
		/// <value>The secret key ring path.</value>
		protected string SecretKeyRingPath {
			get; private set;
		}

		/// <summary>
		/// Gets the public keyring bundle.
		/// </summary>
		/// <value>The public keyring bundle.</value>
		public PgpPublicKeyRingBundle PublicKeyRingBundle {
			get; protected set;
		}

		/// <summary>
		/// Gets the secret keyring bundle.
		/// </summary>
		/// <value>The secret keyring bundle.</value>
		public PgpSecretKeyRingBundle SecretKeyRingBundle {
			get; protected set;
		}

		/// <summary>
		/// Gets the signature protocol.
		/// </summary>
		/// <value>The signature protocol.</value>
		public override string SignatureProtocol {
			get { return "application/pgp-signature"; }
		}

		/// <summary>
		/// Gets the encryption protocol.
		/// </summary>
		/// <value>The encryption protocol.</value>
		public override string EncryptionProtocol {
			get { return "application/pgp-encrypted"; }
		}

		/// <summary>
		/// Gets the key exchange protocol.
		/// </summary>
		/// <value>The key exchange protocol.</value>
		public override string KeyExchangeProtocol {
			get { return "application/pgp-keys"; }
		}

		/// <summary>
		/// Checks whether or not the specified protocol is supported by the <see cref="CryptographyContext"/>.
		/// </summary>
		/// <returns><c>true</c> if the protocol is supported; otherwise <c>false</c></returns>
		/// <param name="protocol">The protocol.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="protocol"/> is <c>null</c>.
		/// </exception>
		public override bool Supports (string protocol)
		{
			if (protocol == null)
				throw new ArgumentNullException ("protocol");

			var type = protocol.ToLowerInvariant ().Split (new char[] { '/' });
			if (type.Length != 2 || type[0] != "application")
				return false;

			if (type[1].StartsWith ("x-", StringComparison.Ordinal))
				type[1] = type[1].Substring (2);

			return type[1] == "pgp-signature" || type[1] == "pgp-encrypted" || type[1] == "pgp-keys";
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.Cryptography.OpenPgpContext"/> class.
		/// </summary>
		/// <param name="pubring">The public keyring file path.</param>
		/// <param name="secring">The secret keyring file path.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="pubring"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="secring"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An error occurred while reading one of the keyring files.
		/// </exception>
		/// <exception cref="Org.BouncyCastle.Bcpg.OpenPgp.PgpException">
		/// An error occurred while parsing one of the keyring files.
		/// </exception>
		protected OpenPgpContext (string pubring, string secring)
		{
			if (pubring == null)
				throw new ArgumentNullException ("pubring");

			if (secring == null)
				throw new ArgumentNullException ("secring");

			PublicKeyRingPath = pubring;
			SecretKeyRingPath = secring;

			if (File.Exists (pubring)) {
				using (var file = File.OpenRead (pubring)) {
					PublicKeyRingBundle = new PgpPublicKeyRingBundle (file);
				}
			} else {
				PublicKeyRingBundle = new PgpPublicKeyRingBundle (new byte[0]);
			}

			if (File.Exists (secring)) {
				using (var file = File.OpenRead (secring)) {
					SecretKeyRingBundle = new PgpSecretKeyRingBundle (file);
				}
			} else {
				SecretKeyRingBundle = new PgpSecretKeyRingBundle (new byte[0]);
			}
		}

		/// <summary>
		/// Gets the encryption key associated with the <see cref="MimeKit.MailboxAddress"/>.
		/// </summary>
		/// <returns>The encryption key.</returns>
		/// <param name="mailbox">The mailbox.</param>
		/// <exception cref="CertificateNotFoundException">
		/// An encryption key for the specified <paramref name="mailbox"/> could not be found.
		/// </exception>
		protected virtual PgpPublicKey GetEncryptionKey (MailboxAddress mailbox)
		{
			// FIXME: do the mailbox comparisons ourselves?
			foreach (PgpPublicKeyRing keyring in PublicKeyRingBundle.GetKeyRings (mailbox.Address, true)) {
				foreach (PgpPublicKey key in keyring.GetPublicKeys ()) {
					if (!key.IsEncryptionKey || key.IsRevoked ())
						continue;

					long seconds = key.GetValidSeconds ();
					if (seconds != 0) {
						var expires = key.CreationTime.AddSeconds ((double) seconds);
						if (expires >= DateTime.Now)
							continue;
					}

					return key;
				}
			}

			throw new CertificateNotFoundException (mailbox, "A valid encryption key could not be found.");
		}

		/// <summary>
		/// Gets the encryption keys for the specified <see cref="MimeKit.MailboxAddress"/>es.
		/// </summary>
		/// <returns>The encryption keys.</returns>
		/// <param name="mailboxes">The mailboxes.</param>
		/// <exception cref="CertificateNotFoundException">
		/// An encryption key for one or more of the <paramref name="mailboxes"/> could not be found.
		/// </exception>
		protected virtual IList<PgpPublicKey> GetEncryptionKeys (IEnumerable<MailboxAddress> mailboxes)
		{
			var recipients = new List<PgpPublicKey> ();

			foreach (var mailbox in mailboxes)
				recipients.Add (GetEncryptionKey (mailbox));

			return recipients;
		}

		/// <summary>
		/// Gets the signing key associated with the <see cref="MimeKit.MailboxAddress"/>.
		/// </summary>
		/// <returns>The signing key.</returns>
		/// <param name="mailbox">The mailbox.</param>
		/// <exception cref="CertificateNotFoundException">
		/// A signing key for the specified <paramref name="mailbox"/> could not be found.
		/// </exception>
		protected virtual PgpSecretKey GetSigningKey (MailboxAddress mailbox)
		{
			foreach (PgpSecretKeyRing keyring in PublicKeyRingBundle.GetKeyRings (mailbox.Address, true)) {
				foreach (PgpSecretKey key in keyring.GetSecretKeys ()) {
					if (!key.IsSigningKey)
						continue;

					var pubkey = key.PublicKey;
					if (pubkey.IsRevoked ())
						continue;

					long seconds = pubkey.GetValidSeconds ();
					if (seconds != 0) {
						var expires = pubkey.CreationTime.AddSeconds ((double) seconds);
						if (expires >= DateTime.Now)
							continue;
					}

					return key;
				}
			}

			throw new CertificateNotFoundException (mailbox, "A valid secret signing key could not be found.");
		}

		/// <summary>
		/// Gets the private key from the specified secret key.
		/// </summary>
		/// <returns>The private key.</returns>
		/// <param name="key">The secret key.</param>
		protected virtual PgpPrivateKey GetPrivateKey (PgpSecretKey key)
		{
			// FIXME: we need a way to query for a passphrase...
			return key.ExtractPrivateKey ("passphrase".ToCharArray ());
		}

		/// <summary>
		/// Gets the private key.
		/// </summary>
		/// <returns>The private key.</returns>
		/// <param name="keyId">The key identifier.</param>
		protected virtual PgpPrivateKey GetPrivateKey (long keyId)
		{
			foreach (PgpSecretKeyRing keyring in SecretKeyRingBundle.GetKeyRings ()) {
				foreach (PgpSecretKey key in keyring.GetSecretKeys ()) {
					if (key.KeyId != keyId)
						continue;

					return GetPrivateKey (key);
				}
			}

			throw new CertificateNotFoundException (keyId.ToString ("X"), "A valid secret signing key could not be found.");
		}

		/// <summary>
		/// Gets the equivalent <see cref="Org.BouncyCastle.Bcpg.HashAlgorithmTag"/> for the specified <see cref="DigestAlgorithm"/>. 
		/// </summary>
		/// <returns>The hash algorithm.</returns>
		/// <param name="digestAlgo">The digest algorithm.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="digestAlgo"/> is out of range.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <paramref name="digestAlgo"/> does not have an equivalent <see cref="Org.BouncyCastle.Bcpg.HashAlgorithmTag"/> value.
		/// </exception>
		public static HashAlgorithmTag GetHashAlgorithm (DigestAlgorithm digestAlgo)
		{
			switch (digestAlgo) {
			case DigestAlgorithm.MD5:       return HashAlgorithmTag.MD5;
			case DigestAlgorithm.Sha1:      return HashAlgorithmTag.Sha1;
			case DigestAlgorithm.RipeMD160: return HashAlgorithmTag.RipeMD160;
			case DigestAlgorithm.DoubleSha: return HashAlgorithmTag.DoubleSha;
			case DigestAlgorithm.MD2:       return HashAlgorithmTag.MD2;
			case DigestAlgorithm.Tiger192:  return HashAlgorithmTag.Tiger192;
			case DigestAlgorithm.Haval5160: return HashAlgorithmTag.Haval5pass160;
			case DigestAlgorithm.Sha256:    return HashAlgorithmTag.Sha256;
			case DigestAlgorithm.Sha384:    return HashAlgorithmTag.Sha384;
			case DigestAlgorithm.Sha512:    return HashAlgorithmTag.Sha512;
			case DigestAlgorithm.Sha224:    return HashAlgorithmTag.Sha224;
			case DigestAlgorithm.MD4: throw new NotSupportedException ("The MD4 digest algorithm is not supported.");
			default: throw new ArgumentOutOfRangeException ("digestAlgo");
			}
		}

		/// <summary>
		/// Sign the content using the specified signer.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.MimePart"/> instance
		/// containing the detached signature data.</returns>
		/// <param name="signer">The signer.</param>
		/// <param name="digestAlgo">The digest algorithm to use for signing.</param>
		/// <param name="content">The content.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="signer"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="content"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="digestAlgo"/> is out of range.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The specified <see cref="DigestAlgorithm"/> is not supported by this context.
		/// </exception>
		/// <exception cref="CertificateNotFoundException">
		/// A signing key could not be found for <paramref name="signer"/>.
		/// </exception>
		public override MimePart Sign (MailboxAddress signer, DigestAlgorithm digestAlgo, byte[] content)
		{
			if (signer == null)
				throw new ArgumentNullException ("signer");

			if (content == null)
				throw new ArgumentNullException ("content");

			var hashAlgorithm = GetHashAlgorithm (digestAlgo);
			var key = GetSigningKey (signer);

			return Sign (key, hashAlgorithm, content);
		}

		/// <summary>
		/// Sign the content using the specified signer.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.MimePart"/> instance
		/// containing the detached signature data.</returns>
		/// <param name="signer">The signer.</param>
		/// <param name="hashAlgorithm">The hashing algorithm to use for signing.</param>
		/// <param name="content">The content.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="signer"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="content"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="signer"/> cannot be used for signing.
		/// </exception>
		public ApplicationPgpSignature Sign (PgpSecretKey signer, HashAlgorithmTag hashAlgorithm, byte[] content)
		{
			if (signer == null)
				throw new ArgumentNullException ("signer");

			if (!signer.IsSigningKey)
				throw new ArgumentException ("The specified secret key cannot be used for signing.", "signer");

			if (content == null)
				throw new ArgumentNullException ("content");

			var memory = new MemoryStream ();

			using (var armored = new ArmoredOutputStream (memory)) {
				var compresser = new PgpCompressedDataGenerator (CompressionAlgorithmTag.ZLib);
				using (var compressed = compresser.Open (armored)) {
					var signatureGenerator = new PgpSignatureGenerator (signer.PublicKey.Algorithm, hashAlgorithm);
					signatureGenerator.InitSign (PgpSignature.CanonicalTextDocument, GetPrivateKey (signer));
					signatureGenerator.Update (content, 0, content.Length);
					var signature = signatureGenerator.Generate ();

					signature.Encode (compressed);
					compressed.Flush ();
				}

				armored.Flush ();
			}

			memory.Position = 0;

			return new ApplicationPgpSignature (memory);
		}

		/// <summary>
		/// Gets the equivalent <see cref="DigestAlgorithm"/> for the specified <see cref="Org.BouncyCastle.Bcpg.HashAlgorithmTag"/>. 
		/// </summary>
		/// <returns>The digest algorithm.</returns>
		/// <param name="hashAlgorithm">The hash algorithm.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="hashAlgorithm"/> is out of range.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <paramref name="hashAlgorithm"/> does not have an equivalent <see cref="DigestAlgorithm"/> value.
		/// </exception>
		public static DigestAlgorithm GetDigestAlgorithm (HashAlgorithmTag hashAlgorithm)
		{
			switch (hashAlgorithm) {
			case HashAlgorithmTag.MD5:           return DigestAlgorithm.MD5;
			case HashAlgorithmTag.Sha1:          return DigestAlgorithm.Sha1;
			case HashAlgorithmTag.RipeMD160:     return DigestAlgorithm.RipeMD160;
			case HashAlgorithmTag.DoubleSha:     return DigestAlgorithm.DoubleSha;
			case HashAlgorithmTag.MD2:           return DigestAlgorithm.MD2;
			case HashAlgorithmTag.Tiger192:      return DigestAlgorithm.Tiger192;
			case HashAlgorithmTag.Haval5pass160: return DigestAlgorithm.Haval5160;
			case HashAlgorithmTag.Sha256:        return DigestAlgorithm.Sha256;
			case HashAlgorithmTag.Sha384:        return DigestAlgorithm.Sha384;
			case HashAlgorithmTag.Sha512:        return DigestAlgorithm.Sha512;
			case HashAlgorithmTag.Sha224:        return DigestAlgorithm.Sha224;
			default: throw new ArgumentOutOfRangeException ("hashAlgorithm");
			}
		}

		/// <summary>
		/// Gets the equivalent <see cref="PublicKeyAlgorithm"/> for the specified <see cref="Org.BouncyCastle.Bcpg.PublicKeyAlgorithmTag"/>. 
		/// </summary>
		/// <returns>The public-key algorithm.</returns>
		/// <param name="algorithm">The public-key algorithm.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="algorithm"/> is out of range.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <paramref name="algorithm"/> does not have an equivalent <see cref="PublicKeyAlgorithm"/> value.
		/// </exception>
		public static PublicKeyAlgorithm GetPublicKeyAlgorithm (PublicKeyAlgorithmTag algorithm)
		{
			switch (algorithm) {
			case PublicKeyAlgorithmTag.RsaGeneral:     return PublicKeyAlgorithm.RsaGeneral;
			case PublicKeyAlgorithmTag.RsaEncrypt:     return PublicKeyAlgorithm.RsaEncrypt;
			case PublicKeyAlgorithmTag.RsaSign:        return PublicKeyAlgorithm.RsaSign;
			case PublicKeyAlgorithmTag.ElGamalEncrypt: return PublicKeyAlgorithm.ElGamalEncrypt;
			case PublicKeyAlgorithmTag.Dsa:            return PublicKeyAlgorithm.Dsa;
			case PublicKeyAlgorithmTag.EC:             return PublicKeyAlgorithm.EllipticCurve;
			case PublicKeyAlgorithmTag.ECDsa:          return PublicKeyAlgorithm.EllipticCurveDsa;
			case PublicKeyAlgorithmTag.ElGamalGeneral: return PublicKeyAlgorithm.ElGamalGeneral;
			case PublicKeyAlgorithmTag.DiffieHellman:  return PublicKeyAlgorithm.DiffieHellman;
			default: throw new ArgumentOutOfRangeException ("algorithm");
			}
		}

		IList<IDigitalSignature> GetDigitalSignatures (PgpSignatureList signatureList, byte[] content, int length)
		{
			var signatures = new List<IDigitalSignature> ();

			for (int i = 0; i < signatureList.Count; i++) {
				var pubkey = PublicKeyRingBundle.GetPublicKey (signatureList[i].KeyId);
				var signature = new OpenPgpDigitalSignature (pubkey) {
					PublicKeyAlgorithm = GetPublicKeyAlgorithm (signatureList[i].KeyAlgorithm),
					DigestAlgorithm = GetDigestAlgorithm (signatureList[i].HashAlgorithm),
					CreationDate = signatureList[i].CreationTime,
				};

				if (pubkey != null) {
					signatureList[i].InitVerify (pubkey);
					signatureList[i].Update (content, 0, length);
					if (signatureList[i].Verify ())
						signature.Status = DigitalSignatureStatus.Good;
					else
						signature.Status = DigitalSignatureStatus.Bad;
				} else {
					signature.Errors = DigitalSignatureError.NoPublicKey;
					signature.Status = DigitalSignatureStatus.Error;
				}

				signatures.Add (signature);
			}

			return signatures;
		}

		/// <summary>
		/// Verify the specified content and signatureData.
		/// </summary>
		/// <returns>A list of digital signatures.</returns>
		/// <param name="content">The content.</param>
		/// <param name="signatureData">The signature data.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="content"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="signatureData"/> is <c>null</c>.</para>
		/// </exception>
		public override IList<IDigitalSignature> Verify (byte[] content, byte[] signatureData)
		{
			if (content == null)
				throw new ArgumentNullException ("content");

			if (signatureData == null)
				throw new ArgumentNullException ("signatureData");

			using (var decoder = PgpUtilities.GetDecoderStream (new MemoryStream (signatureData, false))) {
				var factory = new PgpObjectFactory (decoder);
				var data = factory.NextPgpObject ();
				PgpSignatureList signatureList;

				var compressed = data as PgpCompressedData;
				if (compressed != null) {
					factory = new PgpObjectFactory (compressed.GetDataStream ());
					signatureList = (PgpSignatureList) factory.NextPgpObject ();
				} else {
					if ((signatureList = data as PgpSignatureList) == null)
						throw new Exception ("Unexpected pgp object");
				}

				return GetDigitalSignatures (signatureList, content, content.Length);
			}
		}

		/// <summary>
		/// Encrypts the specified content for the specified recipients.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.MimePart"/> instance
		/// containing the encrypted data.</returns>
		/// <param name="recipients">The recipients.</param>
		/// <param name="content">The content.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="recipients"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="content"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the recipient keys cannot be used for encrypting.</para>
		/// <para>-or-</para>
		/// <para>No recipients were specified.</para>
		/// </exception>
		/// <exception cref="CertificateNotFoundException">
		/// A public key could not be found for one or more of the <paramref name="recipients"/>.
		/// </exception>
		public override MimePart Encrypt (IEnumerable<MailboxAddress> recipients, byte[] content)
		{
			if (recipients == null)
				throw new ArgumentNullException ("recipients");

			if (content == null)
				throw new ArgumentNullException ("content");

			// FIXME: document the exceptions that can be thrown by BouncyCastle
			return Encrypt (GetEncryptionKeys (recipients), content);
		}

		/// <summary>
		/// Encrypts the specified content for the specified recipients.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.MimePart"/> instance
		/// containing the encrypted data.</returns>
		/// <param name="recipients">The recipients.</param>
		/// <param name="content">The content.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="recipients"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="content"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the recipient keys cannot be used for encrypting.</para>
		/// <para>-or-</para>
		/// <para>No recipients were specified.</para>
		/// </exception>
		public MimePart Encrypt (IEnumerable<PgpPublicKey> recipients, byte[] content)
		{
			if (recipients == null)
				throw new ArgumentNullException ("recipients");

			if (content == null)
				throw new ArgumentNullException ("content");

			var memory = new MemoryStream ();

			using (var armored = new ArmoredOutputStream (memory)) {
				var encrypter = new PgpEncryptedDataGenerator (SymmetricKeyAlgorithmTag.Aes256, true);
				int count = 0;

				foreach (var recipient in recipients) {
					if (!recipient.IsEncryptionKey)
						throw new ArgumentException ("One or more of the recipient keys cannot be used for encrypting.", "recipients");

					encrypter.AddMethod (recipient);
					count++;
				}

				if (count == 0)
					throw new ArgumentException ("No recipients specified.", "recipients");

				// FIXME: 0 is the wrong value...
				using (var encrypted = encrypter.Open (armored, 0)) {
					var compresser = new PgpCompressedDataGenerator (CompressionAlgorithmTag.ZLib);

					using (var compressed = compresser.Open (encrypted)) {
						var literalGenerator = new PgpLiteralDataGenerator ();

						using (var literal = literalGenerator.Open (compressed, 't', "mime.txt", content.Length, DateTime.Now)) {
							literal.Write (content, 0, content.Length);
							literal.Flush ();
						}

						compressed.Flush ();
					}

					encrypted.Flush ();
				}

				armored.Flush ();
			}

			memory.Position = 0;

			return new MimePart ("application", "octet-stream") {
				ContentDisposition = new ContentDisposition ("attachment"),
				ContentObject = new ContentObject (memory, ContentEncoding.Default),
			};
		}

		/// <summary>
		/// Signs and encrypts the specified content for the specified recipients.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.MimePart"/> instance
		/// containing the encrypted data.</returns>
		/// <param name="signer">The signer.</param>
		/// <param name="digestAlgo">The digest algorithm to use for signing.</param>
		/// <param name="recipients">The recipients.</param>
		/// <param name="content">The content.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="signer"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="recipients"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="content"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="digestAlgo"/> is out of range.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the recipient keys cannot be used for encrypting.</para>
		/// <para>-or-</para>
		/// <para>No recipients were specified.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The specified <see cref="DigestAlgorithm"/> is not supported by this context.
		/// </exception>
		/// <exception cref="CertificateNotFoundException">
		/// <para>A signing key could not be found for <paramref name="signer"/>.</para>
		/// <para>-or-</para>
		/// <para>A public key could not be found for one or more of the <paramref name="recipients"/>.</para>
		/// </exception>
		public override MimePart SignAndEncrypt (MailboxAddress signer, DigestAlgorithm digestAlgo, IEnumerable<MailboxAddress> recipients, byte[] content)
		{
			if (signer == null)
				throw new ArgumentNullException ("signer");

			if (recipients == null)
				throw new ArgumentNullException ("recipients");

			if (content == null)
				throw new ArgumentNullException ("content");

			var hashAlgorithm = GetHashAlgorithm (digestAlgo);
			var key = GetSigningKey (signer);

			return SignAndEncrypt (key, hashAlgorithm, GetEncryptionKeys (recipients), content);
		}

		/// <summary>
		/// Signs and encrypts the specified content for the specified recipients.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.MimePart"/> instance
		/// containing the encrypted data.</returns>
		/// <param name="signer">The signer.</param>
		/// <param name="hashAlgorithm">The hashing algorithm to use for signing.</param>
		/// <param name="recipients">The recipients.</param>
		/// <param name="content">The content.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="signer"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="recipients"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="content"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="signer"/> cannot be used for signing.</para>
		/// <para>-or-</para>
		/// <para>One or more of the recipient keys cannot be used for encrypting.</para>
		/// <para>-or-</para>
		/// <para>No recipients were specified.</para>
		/// </exception>
		public MimePart SignAndEncrypt (PgpSecretKey signer, HashAlgorithmTag hashAlgorithm, IEnumerable<PgpPublicKey> recipients, byte[] content)
		{
			// FIXME: document the exceptions that can be thrown by BouncyCastle

			if (signer == null)
				throw new ArgumentNullException ("signer");

			if (!signer.IsSigningKey)
				throw new ArgumentException ("The specified secret key cannot be used for signing.", "signer");

			if (recipients == null)
				throw new ArgumentNullException ("recipients");

			if (content == null)
				throw new ArgumentNullException ("content");

			var memory = new MemoryStream ();

			using (var armored = new ArmoredOutputStream (memory)) {
				var encrypter = new PgpEncryptedDataGenerator (SymmetricKeyAlgorithmTag.Aes256, true);
				int count = 0;

				foreach (var recipient in recipients) {
					if (!recipient.IsEncryptionKey)
						throw new ArgumentException ("One or more of the recipient keys cannot be used for encrypting.", "recipients");

					encrypter.AddMethod (recipient);
					count++;
				}

				if (count == 0)
					throw new ArgumentException ("No recipients specified.", "recipients");

				// FIXME: 0 is the wrong value...
				using (var encrypted = encrypter.Open (armored, 0)) {
					var compresser = new PgpCompressedDataGenerator (CompressionAlgorithmTag.ZLib);

					using (var compressed = compresser.Open (encrypted)) {
						var signatureGenerator = new PgpSignatureGenerator (signer.PublicKey.Algorithm, hashAlgorithm);
						signatureGenerator.InitSign (PgpSignature.CanonicalTextDocument, GetPrivateKey (signer));
						var subpacket = new PgpSignatureSubpacketGenerator ();

						foreach (string userId in signer.PublicKey.GetUserIds ()) {
							subpacket.SetSignerUserId (false, userId);
							break;
						}

						signatureGenerator.SetHashedSubpackets (subpacket.Generate ());

						var onepass = signatureGenerator.GenerateOnePassVersion (false);
						onepass.Encode (compressed);

						var literalGenerator = new PgpLiteralDataGenerator ();
						using (var literal = literalGenerator.Open (compressed, 't', "mime.txt", content.Length, DateTime.Now)) {
							signatureGenerator.Update (content, 0, content.Length);
							literal.Write (content, 0, content.Length);
							literal.Flush ();
						}

						var signature = signatureGenerator.Generate ();
						signature.Encode (compressed);

						compressed.Flush ();
					}

					encrypted.Flush ();
				}

				armored.Flush ();
			}

			memory.Position = 0;

			return new MimePart ("application", "octet-stream") {
				ContentDisposition = new ContentDisposition ("attachment"),
				ContentObject = new ContentObject (memory, ContentEncoding.Default)
			};
		}

		/// <summary>
		/// Decrypt the specified encryptedData.
		/// </summary>
		/// <returns>The decrypted <see cref="MimeKit.MimeEntity"/>.</returns>
		/// <param name="encryptedData">The encrypted data.</param>
		/// <param name="signatures">A list of digital signatures if the data was both signed and encrypted.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="encryptedData"/> is <c>null</c>.
		/// </exception>
		public override MimeEntity Decrypt (byte[] encryptedData, out IList<IDigitalSignature> signatures)
		{
			if (encryptedData == null)
				throw new ArgumentNullException ("encryptedData");

			// FIXME: document the exceptions that can be thrown by BouncyCastle
			using (var decoder = PgpUtilities.GetDecoderStream (new MemoryStream (encryptedData, false))) {
				var factory = new PgpObjectFactory (decoder);
				var obj = factory.NextPgpObject ();
				var list = obj as PgpEncryptedDataList;

				if (list == null) {
					// probably a PgpMarker...
					obj = factory.NextPgpObject ();

					list = obj as PgpEncryptedDataList;
					if (list == null)
						throw new Exception ("Unexpected pgp object");
				}

				PgpPublicKeyEncryptedData encrypted = null;
				foreach (PgpEncryptedData data in list.GetEncryptedDataObjects ()) {
					if ((encrypted = data as PgpPublicKeyEncryptedData) != null)
						break;
				}

				if (encrypted == null)
					throw new Exception ("no encrypted data objects found?");

				factory = new PgpObjectFactory (encrypted.GetDataStream (GetPrivateKey (encrypted.KeyId)));
				PgpOnePassSignatureList onepassList = null;
				PgpSignatureList signatureList = null;
				PgpCompressedData compressed = null;
				var memory = new MemoryStream ();

				obj = factory.NextPgpObject ();
				while (obj != null) {
					if (obj is PgpCompressedData) {
						if (compressed != null)
							throw new Exception ("recursive compression detected.");

						compressed = (PgpCompressedData) obj;
						factory = new PgpObjectFactory (compressed.GetDataStream ());
					} else if (obj is PgpOnePassSignatureList) {
						onepassList = (PgpOnePassSignatureList) obj;
					} else if (obj is PgpSignatureList) {
						signatureList = (PgpSignatureList) obj;
					} else if (obj is PgpLiteralData) {
						var literal = (PgpLiteralData) obj;

						using (var stream = literal.GetDataStream ()) {
							var buf = new byte[4096];
							int nread;

							while ((nread = stream.Read (buf, 0, buf.Length)) > 0)
								memory.Write (buf, 0, nread);
						}
					}

					obj = factory.NextPgpObject ();
				}

				memory.Position = 0;

				// FIXME: validate the OnePass signatures... and do what with them?
//				if (onepassList != null) {
//					for (int i = 0; i < onepassList.Count; i++) {
//						var onepass = onepassList[i];
//					}
//				}

				if (signatureList != null) {
					var content = memory.GetBuffer ();

					signatures = GetDigitalSignatures (signatureList, content, (int) memory.Length);
				} else {
					signatures = null;
				}

				var parser = new MimeParser (memory, MimeFormat.Entity);

				return parser.ParseEntity ();
			}
		}

		/// <summary>
		/// Saves the public key ring.
		/// </summary>
		protected void SavePublicKeyRing ()
		{
			var filename = Path.GetFileName (PublicKeyRingPath) + "~";
			var dirname = Path.GetDirectoryName (PublicKeyRingPath);
			var tmp = Path.Combine (dirname, "." + filename);
			var bak = Path.Combine (dirname, filename);

			if (!Directory.Exists (dirname))
				Directory.CreateDirectory (dirname);

			using (var file = File.OpenWrite (tmp)) {
				PublicKeyRingBundle.Encode (file);
			}

			File.Replace (tmp, PublicKeyRingPath, bak);
		}

		/// <summary>
		/// Imports keys (or certificates).
		/// </summary>
		/// <param name="rawData">The raw key data.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="rawData"/> is <c>null</c>.
		/// </exception>
		public override void ImportKeys (byte[] rawData)
		{
			if (rawData == null)
				throw new ArgumentNullException ("rawData");

			using (var memory = new MemoryStream (rawData, false)) {
				using (var armored = new ArmoredInputStream (memory)) {
					var imported = new PgpPublicKeyRingBundle (armored);
					if (imported.Count == 0)
						return;

					var keyrings = new List<PgpPublicKeyRing> ();

					foreach (PgpPublicKeyRing keyring in PublicKeyRingBundle.GetKeyRings ())
						keyrings.Add (keyring);

					foreach (PgpPublicKeyRing keyring in imported.GetKeyRings ())
						keyrings.Add (keyring);

					PublicKeyRingBundle = new PgpPublicKeyRingBundle (keyrings);
					SavePublicKeyRing ();
				}
			}
		}

		/// <summary>
		/// Exports the keys for the specified mailboxes.
		/// </summary>
		/// <returns>The mailboxes.</returns>
		/// <param name="mailboxes">The mailboxes.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="mailboxes"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="mailboxes"/> was empty.
		/// </exception>
		public override MimePart ExportKeys (IEnumerable<MailboxAddress> mailboxes)
		{
			if (mailboxes == null)
				throw new ArgumentNullException ("mailboxes");

			return ExportKeys (GetEncryptionKeys (mailboxes));
		}

		/// <summary>
		/// Exports the specified certificates.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.Cryptography.ApplicationPkcs7Mime"/> instance containing
		/// the exported keys.</returns>
		/// <param name="keys">The keys.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="keys"/> is <c>null</c>.
		/// </exception>
		public MimePart ExportKeys (IEnumerable<PgpPublicKey> keys)
		{
			if (keys == null)
				throw new ArgumentNullException ("keys");

			var keyrings = keys.Select (key => new PgpPublicKeyRing (key.GetEncoded ()));
			var bundle = new PgpPublicKeyRingBundle (keyrings);

			return ExportKeys (bundle);
		}

		/// <summary>
		/// Exports the specified certificates.
		/// </summary>
		/// <returns>A new <see cref="MimeKit.Cryptography.ApplicationPkcs7Mime"/> instance containing
		/// the exported keys.</returns>
		/// <param name="keys">The keys.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="keys"/> is <c>null</c>.
		/// </exception>
		public MimePart ExportKeys (PgpPublicKeyRingBundle keys)
		{
			if (keys == null)
				throw new ArgumentNullException ("keys");

			var content = new MemoryStream ();

			using (var armored = new ArmoredOutputStream (content)) {
				keys.Encode (armored);
				armored.Flush ();
			}

			content.Position = 0;

			return new MimePart ("application", "pgp-keys") {
				ContentDisposition = new ContentDisposition ("attachment"),
				ContentObject = new ContentObject (content, ContentEncoding.Default)
			};
		}
	}
}
