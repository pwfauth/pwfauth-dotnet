"""Run production-pin tests, then legacy protocol tests with a disposable test key.

Requires Python cryptography and dotnet. The production source tree is never
modified. The temporary key exists only in a temporary copy and is not packaged.
"""
from pathlib import Path
import base64
import shutil
import subprocess
import tempfile
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import Encoding, PrivateFormat, NoEncryption

root = Path(__file__).resolve().parents[1]
project = 'tests/PWFAuth.Tests/PWFAuth.Tests.csproj'
subprocess.run(['dotnet', 'test', str(root / project), '-c', 'Release', '--filter', 'FullyQualifiedName~ServerAuthTests'], check=True)
key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
modulus = base64.b64encode(key.public_key().public_numbers().n.to_bytes(384, 'big')).decode()
with tempfile.TemporaryDirectory(prefix='pwfauth-dotnet-regression-') as directory:
    temp = Path(directory).resolve()
    assert temp.parent == Path(tempfile.gettempdir()).resolve() and temp.name.startswith('pwfauth-dotnet-regression-')
    shutil.copytree(root, temp, dirs_exist_ok=True, ignore=shutil.ignore_patterns('.git', 'bin', 'obj', '__pycache__'))
    path = temp / 'src/PWFAuth/ServerAuth.cs'
    source = path.read_text()
    import re
    source, count = re.subn(r'Modulus = Convert.FromBase64String\("[A-Za-z0-9+/=]+"\)', 'Modulus = Convert.FromBase64String("' + modulus + '")', source)
    assert count == 1
    path.write_text(source)
    private = base64.b64encode(key.private_bytes(Encoding.DER, PrivateFormat.PKCS8, NoEncryption())).decode()
    path = temp / 'tests/PWFAuth.Tests/TestSupport.cs'
    source = path.read_text().replace('https://license.test', 'https://pwfauth.com')
    source = source.replace('return _respond(recorded);', '''var response = _respond(recorded);
            if (recorded.Header("X-PWF-Nonce") != null) {
                byte[] reply = await response.Content.ReadAsByteArrayAsync();
                string Hash(byte[] value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value)).ToLowerInvariant();
                string material = string.Join("\\n", "PWF-REPLY-V1", recorded.Header("X-PWF-Nonce"), recorded.Method.Method, recorded.Path,
                    Hash(Encoding.UTF8.GetBytes(recorded.Body ?? "")), ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), Hash(reply));
                using var rsa = System.Security.Cryptography.RSA.Create();
                rsa.ImportPkcs8PrivateKey(Convert.FromBase64String("PRIVATE"), out _);
                response.Headers.Add("X-PWF-Signature", Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(material), System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1)));
            }
            return response;'''.replace('PRIVATE', private))
    path.write_text(source)
    subprocess.run(['dotnet', 'test', str(temp / project), '-c', 'Release', '--filter', 'FullyQualifiedName!~ServerAuthTests'], check=True)
