# Microsoft Store Publishing Setup

This guide explains how to configure GitHub Actions to automatically publish to the Microsoft Store.

## Prerequisites

1. A Microsoft Partner Center account
2. An app registered in Partner Center
3. Azure AD app registration for API access

## Step 1: Register Your App in Partner Center

1. Go to [Partner Center](https://partner.microsoft.com/dashboard)
2. Navigate to **Apps and games**
3. Click **New product** > **App**
4. Enter "Markdown Viewer" as the product name
5. Note your **App ID** (you'll need this later)

## Step 2: Create Azure AD App Registration

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** > **App registrations**
3. Click **New registration**
4. Configure:
   - Name: `MarkdownViewer-StorePublisher`
   - Supported account types: Single tenant
5. After creation, note:
   - **Application (client) ID** - This is your `STORE_CLIENT_ID`
   - **Directory (tenant) ID** - This is your `STORE_TENANT_ID`

### Create Client Secret

1. In your app registration, go to **Certificates & secrets**
2. Click **New client secret**
3. Set an expiration and create
4. **Copy the secret value immediately** - This is your `STORE_CLIENT_SECRET`

### Grant API Permissions

1. In your app registration, go to **API permissions**
2. Click **Add a permission**
3. Select **APIs my organization uses**
4. Search for "Windows Store"
5. Add these permissions:
   - `user_impersonation`

## Step 3: Link Azure AD to Partner Center

1. In Partner Center, go to **Account settings** > **User management**
2. Click **Azure AD applications**
3. Click **Add Azure AD application**
4. Select or enter your Azure AD app
5. Assign the **Developer** role (minimum) or **Manager** for full access

## Step 4: Configure GitHub Secrets

In your GitHub repository, go to **Settings** > **Secrets and variables** > **Actions**.

Add these secrets:

| Secret Name | Description |
|-------------|-------------|
| `STORE_TENANT_ID` | Azure AD Directory (tenant) ID |
| `STORE_CLIENT_ID` | Azure AD Application (client) ID |
| `STORE_CLIENT_SECRET` | Azure AD client secret |
| `STORE_APP_ID` | Partner Center App ID |
| `SIGNING_CERTIFICATE` | Base64-encoded PFX certificate (optional) |
| `SIGNING_PASSWORD` | Password for the PFX certificate (optional) |

## Step 5: Create Store Assets

Create these image assets in `MarkdownViewer/Assets/`:

| File | Size | Description |
|------|------|-------------|
| `StoreLogo.png` | 50x50 | Store listing icon |
| `Square44x44Logo.png` | 44x44 | Taskbar icon |
| `Square71x71Logo.png` | 71x71 | Small tile |
| `Square150x150Logo.png` | 150x150 | Medium tile |
| `Wide310x150Logo.png` | 310x150 | Wide tile |
| `Square310x310Logo.png` | 310x310 | Large tile |
| `SplashScreen.png` | 620x300 | Splash screen |
| `FileIcon.png` | 32x32 | File association icon |

### Asset Requirements
- All images must be PNG format
- Use transparency where appropriate
- Follow [Microsoft's asset guidelines](https://docs.microsoft.com/en-us/windows/apps/design/style/iconography/app-icon-construction)

## Step 6: Update Package.appxmanifest

Edit `MarkdownViewer/Package.appxmanifest`:

1. Update the `Identity` element:
   ```xml
   <Identity
     Name="YOUR_PUBLISHER_ID.MarkdownViewer"
     Publisher="CN=YOUR_PUBLISHER_ID"
     ...
   ```

2. Replace `YOUR_PUBLISHER_ID` with your actual Publisher ID from Partner Center

3. Update `PublisherDisplayName` with your company/publisher name

## Step 7: Code Signing (Optional but Recommended)

For a better user experience, sign your MSIX package:

### Create a Self-Signed Certificate (Development)
```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=Your Name" `
  -KeyUsage DigitalSignature -FriendlyName "Markdown Viewer Dev" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

$password = ConvertTo-SecureString -String "YourPassword" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "signing.pfx" -Password $password
```

### Use a Trusted Certificate (Production)

For Store submission, Microsoft signs the package automatically. For sideloading, you'll need a certificate from a trusted CA.

### Add Certificate to GitHub Secrets

```powershell
$pfxPath = "signing.pfx"
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath))
$base64 | Set-Clipboard
```

Paste the result into the `SIGNING_CERTIFICATE` GitHub secret.

## Step 8: Publishing

### Automatic Publishing

The workflow automatically publishes when you push a version tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

### Manual Publishing

1. Go to **Actions** tab in GitHub
2. Select **Build and Publish to Microsoft Store**
3. Click **Run workflow**
4. Check "Submit to Microsoft Store"
5. Click **Run workflow**

## Store Submission Checklist

Before publishing:

- [ ] All required assets created
- [ ] Package.appxmanifest updated with correct publisher info
- [ ] App description added in Partner Center
- [ ] Screenshots uploaded in Partner Center
- [ ] Privacy policy URL set
- [ ] Age rating questionnaire completed
- [ ] Pricing set (free or paid)
- [ ] Markets selected

## Troubleshooting

### "Access denied" when uploading
- Ensure your Azure AD app has the correct permissions
- Verify the app is linked in Partner Center

### "Package validation failed"
- Check all required assets exist
- Verify the manifest Publisher matches your Partner Center publisher ID

### "Submission already exists"
- Wait for the previous submission to complete
- Or cancel the pending submission in Partner Center

## Resources

- [Partner Center Documentation](https://docs.microsoft.com/en-us/windows/uwp/publish/)
- [MSIX Packaging Overview](https://docs.microsoft.com/en-us/windows/msix/)
- [StoreBroker PowerShell Module](https://github.com/microsoft/StoreBroker)
- [Windows App Certification Kit](https://developer.microsoft.com/en-us/windows/downloads/app-certification-kit/)
