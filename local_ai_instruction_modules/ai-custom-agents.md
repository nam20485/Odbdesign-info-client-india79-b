# LLM Client Subagents

This project defines a minimal set of LLM client subagents to support targeted delegation and automation-first workflows on Windows (PowerShell/pwsh). These subagents can be used by any compatible client (e.g., OpenCode, GitHub Copilot, Gemini, Qwen, Factory Droid, Kilo Codex, Traycer).

- Project agents live under `.claude/agents/`.
- Format: Markdown with YAML frontmatter (see files for examples).
- Shell defaults: Windows, prefer PowerShell (pwsh). Avoid bash-only commands.
- Research tasks should be delegated to the Researcher (uses the configured research tooling).

Quick start:

1) In VS Code with your LLM client enabled, open the Agents panel (e.g., `/agents` if supported).
2) The project-level agents will appear; select and use them in chats.
3) Use the Orchestrator to plan, delegate to specialized agents, and approve work.

Primary references:

- `scripts/agent-creation-rules.md` (authoring guide)
- [nam20485/agent-instructions](https://github.com/nam20485/agent-instructions) (canonical instruction modules)

---

## Agent index

Core

- orchestrator — Plans, delegates, approves; avoids direct implementation.
- researcher — Uses the configured research tooling to produce citation-rich briefs.
- code-reviewer — Reviews diffs for correctness, security, performance, and style.

Build & Quality

- qa-test-engineer — Designs and runs tests; validates green builds.
- devops-engineer — CI/CD, reproducible builds, observability basics.
- frontend-developer — UI components/pages with component tests.
- backend-developer — Endpoints/modules with unit/integration tests.

Planning

- planner — Breaks work into tasks with acceptance criteria.
- product-manager — Defines goals, constraints, acceptance criteria.
- scrum-master — Facilitates cadence; removes blockers; enforces DoD.

Specialized

- cloud-infra-expert — Cloud architecture, IaC patterns, security baselines.
- github_ops_expert — Manages GitHub configuration, automation, and policy enforcement.
- performance-optimizer — Profiles and enforces performance budgets.
- security-expert — Threat modeling, secrets hygiene, dependency risk.
- database-admin — Schema/migrations, performance, backup/restore.
- data-scientist — Data pipelines, metrics, experiments, reproducibility.
- ml-engineer — Model training/inference, evaluation, deployment readiness.
- ux-ui-designer — Wireframes, flows, accessibility, design QA.
- mobile-developer — Platform-specific builds and store readiness.
- debugger — Repro steps, minimal failing tests, fix validation.
- developer — Generalist for small, scoped tasks.
- documentation-expert — Writes developer and user docs, quickstarts, and runbooks.
- prompt-engineer — System prompts, tool routing, guardrails.
