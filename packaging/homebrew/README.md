# Homebrew packaging for mylo

`mylo.rb` is the cask. It is kept here, in the repository that builds mylo,
so it changes in the same commit as anything that changes what the cask points
at. It is not installable from here: Homebrew only installs casks out of a
tap.

## Why a cask and not a formula

mylo is a GUI application shipped as `mylo.app`. A formula installs a
command-line program into the Homebrew prefix, which would put nothing in
Spotlight, the Dock or Launchpad. A cask installs the bundle into the
applications directory, which is what a user expects from something with a
window.

## The tap

There is no tap yet. `scottgal/homebrew-stylobot` exists but it is StyloBot's
own tap and mylo does not belong in it.

The tap to create is:

    scottgal/homebrew-tap

That name is not arbitrary. Homebrew maps `brew tap scottgal/tap` onto the
repository `github.com/scottgal/homebrew-tap`, so the `homebrew-` prefix has
to be there and the rest of the name is what a user types. `tap` is the
conventional choice for a personal tap holding more than one thing, which
leaves room for lucidVIEW later without a second repository.

**Creating it is a decision for the maintainer, not something automated here.**
Once it exists:

```bash
# one-off, in a checkout of the new empty tap repository
mkdir -p Casks
cp /path/to/lucidview/packaging/homebrew/mylo.rb Casks/mylo.rb
git add Casks/mylo.rb
git commit -m "Add the mylo cask"
git push
```

and after that, per release, the same copy and commit.

A user then installs with:

```bash
brew tap scottgal/tap
brew install --cask mylo
```

or in one line, `brew install --cask scottgal/tap/mylo`.

## Gatekeeper

mylo is ad-hoc codesigned, not notarised, because notarisation needs a paid
Apple Developer account. Homebrew attaches `com.apple.quarantine` to
everything it downloads, and on a build that is not notarised that attribute
is what produces "mylo.app is damaged and cannot be opened" on the first
launch. It is not damaged; macOS is refusing to open something it cannot
attribute to a registered developer.

The cask's `postflight` removes the attribute from the copy Homebrew has just
installed, and from nothing else. That is the same `xattr -dr
com.apple.quarantine` the manual install instructions already tell a user to
run, and the same decision right-clicking the app and choosing Open makes.
Doing it in the cask only means the user does not meet the dialog first.

A user who would rather make that call themselves has two options, both worth
knowing about:

* `brew install --cask --no-quarantine mylo` never sets the attribute, so the
  postflight has nothing to remove.
* Check the provenance before installing. The `.app` is built in public by
  `.github/workflows/release-mylo.yml` in this repository, from the commit the
  release tag points at.

If mylo is ever notarised, delete the `postflight` block. It becomes
unnecessary and an unnecessary `xattr` in a cask is a thing a reader has to
stop and justify.

## Keeping the checksums honest

Never type a `sha256` by hand. A single wrong character fails at install with
a message about a mismatched download, which reads like the file was tampered
with rather than like the cask is out of date.

Two ways to get them, and they produce the same file:

1. **From the release workflow.** `.github/workflows/release-mylo.yml` has a
   `cask` job that checksums the artifacts it just built, writes a finished
   copy of `mylo.rb`, and attaches it to the draft release as `mylo.rb`. Copy
   that straight into the tap.

2. **After the fact.** `./update-cask.sh mylo-v0.2.3` downloads the two macOS
   zips from that tag, checksums them, and rewrites the version and both
   `sha256` lines in `mylo.rb` in place. With no argument it takes the newest
   `mylo-v*` release. It does not commit and does not touch the tap.

Either way, `brew style packaging/homebrew/mylo.rb` before committing.

## What `brew audit` says, and what to ignore

`brew audit --cask --strict --online` reports one problem:

    mylo-v0.2.2 is a GitHub pre-release.

That is a homebrew-core submission rule, not a defect. mylo's releases are all
marked pre-release below 1.0 on purpose. In a personal tap it does not apply,
and it is why the cask carries an explicit `livecheck` with a tag regex: the
default strategy only looks at the latest full release, so it would never see
a mylo tag at all.

Everything else passes, including the download, the checksum and the presence
of `mylo.app` inside the zip.
