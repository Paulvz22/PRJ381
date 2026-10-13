# Firestore setup, seed data and security rules

Work from this `firebase` directory for the following commands. Use Node.js 22 or newer for these pinned
major versions; use an appropriate JDK for the Firestore emulator (JDK 21 is a suitable choice).
The app's own build still uses the .NET/MAUI toolchain.

## 1. Validate locally - no account needed

```powershell
node scripts/validate.mjs
node --test tests/data.test.mjs
node scripts/seed-locations.mjs --dry-run
```

`locations.seed.json` and `../MAUIProject/BCOpendayApp/Resources/Raw/locations.json` must match exactly
as parsed data. The validator rejects schema drift, duplicate IDs, missing fields, remote image URLs,
and invalid image coordinates before any cloud write.

The Firestore document paths are:

```text
locations/robotics
locations/manufacturing
locations/lecture
locations/library
locations/lounge
```

Each document stores `name`, `image`, `summary`, `description`, `highlight`, `sortOrder`, `mapSection`,
`x`, `y` and `positionNote`. The JSON `id` becomes the document ID, not a stored field.
Images refer to resources already in your MAUI project; this update does not upload image files.
These plain JSON files are not Firebase CLI emulator-export manifests. Use the seeder, not `--import`.

## 2. Install the tooling and test the rules

```powershell
npm install
npm run test:rules
```

This creates `package-lock.json`; retain it in your repository after a successful install.
The supplied package ranges keep the rules-testing library and Firebase JS SDK on compatible major
versions. The emulator tests use the isolated `demo-bcopenday-members34` ID and do not contact production.
The test runner loads the actual `firestore.rules` file and covers authenticated/unauthenticated reads,
all client write types, nested documents, unrelated collections and attempted client admin-claim writes.

## 3. Review and deploy your rules

Enable Anonymous Authentication and create your default Firestore database in your Firebase project.
Review any existing project rules before replacing them: this supplied ruleset denies every collection
except authenticated reads of `locations`.

```powershell
npx firebase login
npx firebase deploy --only firestore:rules --project ACTUAL_FIREBASE_PROJECT_ID
```

Replace `ACTUAL_FIREBASE_PROJECT_ID` with your real project ID. No project ID is hardcoded in this update.
Do not run `firebase init` over these files and accidentally replace the reviewed rules with open test-mode rules.
No custom composite indexes are needed by the catalogue's plain collection read; ordering happens locally.

## 4. Seed using trusted administrator credentials

Use Application Default Credentials with permissions for the chosen Firestore database. For a local
service-account file, keep it OUTSIDE the repository and point the environment variable to it.
Never put an Admin SDK private key in the mobile app, Resources/Raw, appsettings, Git or this ZIP.
Firebase CLI login is not itself the same thing as supplying Admin SDK Application Default Credentials.

```powershell
$env:GOOGLE_APPLICATION_CREDENTIALS = "C:\private\service-account.json"

# Review first. This does not write anything.
npm run seed -- --project ACTUAL_FIREBASE_PROJECT_ID --dry-run

# Explicit apply: create missing documents; preserve existing documents.
npm run seed -- --project ACTUAL_FIREBASE_PROJECT_ID --apply
```

After deliberately changing the JSON and reviewing what should replace existing fields:

```powershell
npm run seed -- --project ACTUAL_FIREBASE_PROJECT_ID --apply --overwrite
```

The transaction is idempotent in default create-missing mode and never deletes documents. Administrator
writes bypass client Firestore Security Rules and rely on IAM; ordinary app visitors cannot use this path.
Confirm the five document IDs in the Firebase console, rebuild with your downloaded mobile config file,
and use Refresh locations in the app.

## Optional: seed a LOCAL emulator instead

In a first PowerShell terminal, start the Firestore emulator:

```powershell
npx firebase emulators:start --only firestore --project demo-bcopenday-members34
```

In a second terminal in the same directory:

```powershell
$env:FIRESTORE_EMULATOR_HOST = "127.0.0.1:8080"
npm run seed -- --project demo-bcopenday-members34 --apply
Remove-Item Env:FIRESTORE_EMULATOR_HOST
```

These commands test the data tooling only. The MAUI app is configured for your real Firebase project when
its platform config is supplied; it is not automatically redirected to the desktop emulator. Rules tests
use emulated authentication contexts and do not need a separate Authentication emulator.
