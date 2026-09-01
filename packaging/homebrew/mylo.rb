# Homebrew cask for mylo.
#
# This file is the source of truth. It is not installable from where it sits:
# Homebrew installs casks from a tap, which is a git repository named
# homebrew-<something>, so this has to be copied into one. See README.md in
# this directory for the tap name to create and the two commands that put it
# there.
#
# A cask rather than a formula because mylo is a GUI application shipped as a
# .app bundle. A formula would build or install a command-line program into
# the Homebrew prefix, which is the wrong shape: nothing would appear in
# Spotlight, in the Dock or in Launchpad.
#
# The sha256 values are generated, not typed. The release workflow
# (.github/workflows/release-mylo.yml) writes a finished copy of this file
# with the checksums of the artifacts it just built and attaches it to the
# draft release, and packaging/homebrew/update-cask.sh rewrites this one in
# place from any published tag. Do not edit them by hand: a wrong checksum
# fails at install time with a message about a mismatch, which reads like a
# compromised download rather than a stale file.
cask "mylo" do
  arch arm: "arm64", intel: "x64"

  version "0.2.4"
  sha256 arm:   "06839bb1d9582dc75a98fca02c3e29093d63399b011e163efd9fc5948d91c781",
         intel: "c9ea5f1ef259afe57aac57cd06027e5540e2b6886ec0276fcc65359ccaf25574"

  url "https://github.com/scottgal/lucidview/releases/download/mylo-v#{version}/mylo-osx-#{arch}.zip",
      verified: "github.com/scottgal/lucidview/"
  name "mylo"
  desc "Native RSS and Atom reader built on the lucidVIEW rendering stack"
  homepage "https://github.com/scottgal/lucidview"

  # mylo's releases are all marked pre-release while the version is below 1.0,
  # so the default livecheck, which only looks at the latest full release,
  # would never see one. This matches the tag shape instead.
  livecheck do
    url :url
    regex(/^mylo[._-]v?(\d+(?:\.\d+)+)$/i)
    strategy :git
  end

  # A bare symbol already means "this version or newer" to the cask DSL, and
  # `brew style` rejects the ">= :big_sur" spelling. 11.0 because that is what
  # LSMinimumSystemVersion in LucidReader/macos/Info.plist says.
  depends_on macos: :big_sur

  app "mylo.app"

  # Gatekeeper. mylo is ad-hoc codesigned, not notarised with an Apple
  # Developer ID, because notarisation needs a paid Apple Developer account.
  # Homebrew attaches the com.apple.quarantine attribute to everything it
  # downloads, and on an app that is not notarised that attribute is what
  # produces "mylo.app is damaged and cannot be opened" or "cannot be opened
  # because the developer cannot be verified" on the first launch.
  #
  # So the attribute is removed here, on the copy Homebrew has just placed in
  # the applications directory and nowhere else. That is the same thing the
  # manual install instructions tell a user to type, and the same thing
  # right-clicking the app and choosing Open does; doing it in the cask only
  # saves them from meeting a scary dialog first.
  #
  # A user who would rather make that call themselves can install with
  # `brew install --cask --no-quarantine mylo`, which skips the attribute
  # being set at all, or read the app's provenance first: it is built in
  # public by the release workflow in this repository, from the commit the
  # tag points at.
  postflight do
    system_command "/usr/bin/xattr",
                   args: ["-dr", "com.apple.quarantine", "#{appdir}/mylo.app"],
                   sudo: false
  end

  uninstall quit: "com.mostlylucid.mylo"

  # Left behind by design on uninstall, removed on `brew uninstall --zap`.
  # reader.db is the user's subscriptions and every article mylo has cached,
  # which is not something an upgrade should be able to throw away.
  zap trash: [
    "~/Library/Application Support/mylo",
    "~/Library/Saved Application State/com.mostlylucid.mylo.savedState",
  ]
end
