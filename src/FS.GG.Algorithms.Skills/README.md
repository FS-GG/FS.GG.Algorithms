# FS.GG.Algorithms.Skills

Content-only owner package: `algorithms-skills/skill-manifest.json` and its complete
`skills/fs-gg-algorithms/` file tree. The manifest is generated from the producer sources;
each file is content-addressed. The package has no runtime assembly or dependencies.

`build/FS.GG.Algorithms.Skills.props` exposes `FsggAlgorithmsSkillsContentDir` for an SDD
build to embed the manifest and **all files**, including the Atlas pack. It does not write
consumer workspaces. Actual materialization belongs to SDD's verified owner-skill planner
and upgrade route. Restoring this package alone does not install the skill or prove an
SDD release has adopted it.

The skill indexes immutable source and explicitly bounded determinism/benchmark evidence.
It does not imply a library package is installed, recently published or benchmarked on
the consumer's host.
