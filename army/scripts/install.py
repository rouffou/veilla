"""Installe l'équipe d'agents dans un projet Claude Code.

Ce que fait le script (aucune IA n'est appelée : c'est de la copie et du remplissage de modèles) :
  1. copie le dossier `army/` dans le projet cible (si ce n'est pas le projet source) ;
  2. génère les agents dans `.claude/agents/` à partir du catalogue AGENTS ci-dessous ;
  3. copie les commandes slash dans `.claude/commands/` ;
  4. crée `.claude/settings.json`, ou fusionne ses permissions avec celles déjà présentes ;
  5. crée `CLAUDE.md` à partir du modèle, ou ajoute l'import de l'orchestrateur s'il existe déjà ;
  6. ajoute les fichiers locaux de Claude Code au `.gitignore`.

Par défaut, rien de ce qui existe déjà n'est écrasé (sauf fusion de settings.json et ajouts à CLAUDE.md / .gitignore).

Usage :
    python army/scripts/install.py                         # installe dans le projet qui contient army/
    python army/scripts/install.py --target C:/chemin/projet
    python army/scripts/install.py --target ../projet --dry-run   # affiche sans rien écrire
    python army/scripts/install.py --force                 # écrase agents, commandes et army/ existants

Pour ajouter un agent : créer son ROLE.md, ajouter une entrée dans AGENTS, relancer le script avec --force.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ARMY = Path(__file__).resolve().parents[1]
SOURCE_ROOT = ARMY.parent
CLAUDE_TEMPLATES = ARMY / "claude"
ORCHESTRATOR_IMPORT = "@army/ORCHESTRATOR.md"
GITIGNORE_LINES = [".claude/settings.local.json", "CLAUDE.local.md"]

# --------------------------------------------------------------------------------------
# Catalogue des agents
# --------------------------------------------------------------------------------------
DOC_TOOLS = "Read, Grep, Glob, Write, Edit, WebSearch, WebFetch"
REVIEW_TOOLS = "Read, Grep, Glob, Bash, Write"
ALL_TOOLS = None  # None = hérite de tous les outils de la session

R = "army/roles"
S = "army/stacks"

STACKS = {
    "dotnet": (".NET / C#", f"{S}/dotnet/STANDARDS.md"),
    "python": ("Python", f"{S}/python/STANDARDS.md"),
    "java": ("Java / Spring", f"{S}/java/STANDARDS.md"),
    "typescript": ("TypeScript / Node.js", f"{S}/typescript/STANDARDS.md"),
}

# (nom, titre, description, outils, modèle, [fichiers à lire], dossier de templates)
AGENTS = [
    ("project-manager", "Chef de projet",
     "Chef de projet. À utiliser pour cadrer un projet, produire une charte, un planning, un registre RAID (risques, actions, issues, décisions), un status report, ou arbitrer coût/délai/périmètre.",
     DOC_TOOLS + ", Bash", None, [f"{R}/01-pilotage/project-manager/ROLE.md"], f"{R}/01-pilotage/project-manager/templates/"),
    ("product-owner", "Product Owner",
     "Product Owner. À utiliser pour formaliser une vision produit, rédiger un PRD, découper en épiques et user stories avec critères d'acceptation Gherkin, définir le MVP et prioriser le backlog.",
     DOC_TOOLS, None, [f"{R}/01-pilotage/product-owner/ROLE.md"], f"{R}/01-pilotage/product-owner/templates/"),
    ("business-analyst", "Business Analyst",
     "Business Analyst. À utiliser pour modéliser des processus métier (AS-IS / TO-BE), formaliser des règles de gestion, construire le glossaire métier et rédiger des spécifications fonctionnelles détaillées.",
     DOC_TOOLS, None, [f"{R}/01-pilotage/business-analyst/ROLE.md"], f"{R}/01-pilotage/business-analyst/templates/"),
    ("enterprise-architect", "Architecte d'entreprise",
     "Architecte d'entreprise (TOGAF/ArchiMate). À utiliser pour évaluer l'alignement d'un projet avec le SI, cartographier les capacités métier, décider build/buy/reuse, définir les principes d'architecture et la roadmap de transformation.",
     DOC_TOOLS, "opus", [f"{R}/02-architecture/enterprise-architect/ROLE.md"], f"{R}/02-architecture/enterprise-architect/templates/"),
    ("solution-architect", "Architecte solution",
     "Architecte solution. À utiliser pour concevoir une solution de bout en bout : exigences non fonctionnelles, diagrammes C4 (contexte, conteneurs), intégrations, choix technologiques et ADR, dossier d'architecture (SAD).",
     DOC_TOOLS, "opus", [f"{R}/02-architecture/solution-architect/ROLE.md"], f"{R}/02-architecture/solution-architect/templates/"),
    ("security-architect", "Architecte sécurité",
     "Architecte sécurité. À utiliser pour un modèle de menaces STRIDE, des exigences de sécurité (OWASP ASVS), la conception IAM (OIDC/OAuth), la conformité (RGPD, NIS2…) ou la revue de code sensible.",
     DOC_TOOLS + ", Bash", "opus", [f"{R}/02-architecture/security-architect/ROLE.md"], f"{R}/02-architecture/security-architect/templates/"),
    ("data-architect", "Architecte données",
     "Architecte données. À utiliser pour concevoir un modèle de données (conceptuel à physique), choisir les bases, définir migrations, flux de données, architecture analytique, classification et rétention RGPD.",
     DOC_TOOLS, "opus", [f"{R}/02-architecture/data-architect/ROLE.md"], f"{R}/02-architecture/data-architect/templates/"),
    ("frontend-developer", "Développeur front-end",
     "Développeur front-end (TypeScript, React/Next.js, Angular). À utiliser pour implémenter des écrans, des composants de design system, l'intégration d'API côté client, l'accessibilité WCAG et la performance web.",
     ALL_TOOLS, None, [f"{R}/03-developpement/frontend-developer/ROLE.md", f"{S}/typescript/STANDARDS.md"], None),
    ("code-reviewer", "Relecteur de code",
     "Relecteur de code. À utiliser après toute modification de code, ou pour relire une PR/branche : correction, sécurité, fiabilité, performance, architecture, tests. Produit un rapport priorisé P0-P3 avec verdict.",
     REVIEW_TOOLS, None, [f"{R}/03-developpement/code-reviewer/ROLE.md"], f"{R}/03-developpement/code-reviewer/templates/"),
    ("qa-engineer", "Ingénieur QA",
     "Ingénieur QA / test. À utiliser pour définir une stratégie et un plan de test, écrire des tests automatisés (API, E2E Playwright, contrat, charge k6), vérifier des critères d'acceptation ou rédiger un rapport de bug.",
     ALL_TOOLS, None, [f"{R}/04-qualite/qa-engineer/ROLE.md"], f"{R}/04-qualite/qa-engineer/templates/"),
    ("devops-engineer", "Ingénieur DevOps",
     "Ingénieur DevOps. À utiliser pour les pipelines CI/CD (GitHub Actions, GitLab CI, Azure DevOps), Dockerfiles, Infrastructure as Code (Terraform, Bicep, CDK), DevSecOps, stratégies de déploiement et de release.",
     ALL_TOOLS, None, [f"{R}/05-operations/devops-engineer/ROLE.md"], f"{R}/05-operations/devops-engineer/templates/"),
    ("cloud-architect", "Architecte / ingénieur cloud",
     "Architecte et ingénieur cloud (Azure, AWS, GCP). À utiliser pour concevoir une landing zone, choisir les services managés, le réseau, la haute disponibilité et la reprise, écrire l'IaC des ressources et estimer les coûts (FinOps).",
     ALL_TOOLS, "opus", [f"{R}/05-operations/cloud-architect/ROLE.md",
                         "Le référentiel du fournisseur concerné : `army/stacks/cloud/azure.md`, `army/stacks/cloud/aws.md` ou `army/stacks/cloud/gcp.md`"],
     f"{R}/05-operations/cloud-architect/templates/"),
    ("sre", "Site Reliability Engineer",
     "SRE. À utiliser pour définir SLI/SLO et budgets d'erreur, concevoir l'observabilité (OpenTelemetry, dashboards, alertes), rédiger des runbooks, gérer un incident de production ou rédiger un post-mortem.",
     ALL_TOOLS, None, [f"{R}/05-operations/sre/ROLE.md"], f"{R}/05-operations/sre/templates/"),
    ("technical-writer", "Rédacteur technique",
     "Rédacteur technique. À utiliser pour écrire ou mettre à jour un README, une documentation d'API, un guide utilisateur ou d'exploitation, un changelog ou des notes de version.",
     "Read, Grep, Glob, Write, Edit, Bash", None, [f"{R}/06-documentation/technical-writer/ROLE.md"], f"{R}/06-documentation/technical-writer/templates/"),
]

for key, (label, standards) in STACKS.items():
    AGENTS.append((
        f"software-architect-{key}", f"Architecte logiciel {label}",
        f"Architecte logiciel {label}. À utiliser pour concevoir la structure interne d'une application {label} : couches, modules, DDD, patterns, contrats d'API, structure du dépôt, stratégie de tests, ou pour auditer une base de code {label} existante.",
        DOC_TOOLS + ", Bash", "opus",
        [f"{R}/02-architecture/software-architect/ROLE.md", standards],
        f"{R}/02-architecture/software-architect/templates/",
    ))
    AGENTS.append((
        f"developer-{key}", f"Développeur {label}",
        f"Développeur back-end {label}. À utiliser pour implémenter une user story, corriger un bug, écrire des tests ou refactorer du code {label}, en respectant la conception et les standards de la stack.",
        ALL_TOOLS, None,
        [f"{R}/03-developpement/developer/ROLE.md", standards],
        None,
    ))


def render_agent(name, title, description, tools, model, files, templates) -> str:
    front = ["---", f"name: {name}", f"description: {description}"]
    if tools:
        front.append(f"tools: {tools}")
    if model:
        front.append(f"model: {model}")
    front.append("---")

    reads = ["`army/shared/conventions.md`", "`army/shared/handoff.md`"]
    reads += [f if f.startswith("Le ") else f"`{f}`" for f in files]
    read_list = "\n".join(f"{i}. {r}" for i, r in enumerate(reads, 1))

    tpl = (f"\nUtilise les templates de `{templates}` (et `army/shared/templates/`) pour chaque livrable.\n"
           if templates else "\nUtilise `army/shared/templates/pr-description.md` pour décrire tes changements.\n")

    body = f"""
Tu es **{title}** au sein d'une équipe d'agents spécialisés coordonnée par un orchestrateur.

## Chargement obligatoire du contexte
Avant toute action, lis ces fichiers dans cet ordre et applique-les strictement :
{read_list}

Lis aussi la section « Projet » et « Conventions spécifiques au projet » du `CLAUDE.md` : elles priment sur les standards génériques.
{tpl}
## Règles
- Reste dans ton périmètre (section « Hors périmètre » de ton ROLE.md). Ce qui n'en relève pas est signalé dans le HANDOFF avec l'agent compétent.
- Explore le dépôt et la documentation existante (`docs/`) avant de produire quoi que ce soit.
- Écris tes livrables dans `docs/` selon `army/shared/conventions.md` ; ne laisse jamais un livrable uniquement dans ta réponse.
- Si une information bloquante manque, arrête-toi et pose la question dans le HANDOFF (statut `bloqué`).
- Aucune action irréversible (production, suppression, dépense, push) sans confirmation explicite de l'utilisateur.
- Termine **toujours** par le bloc HANDOFF défini dans `army/shared/handoff.md`.
"""
    return "\n".join(front) + "\n" + body


# --------------------------------------------------------------------------------------
# Installation
# --------------------------------------------------------------------------------------
class Installer:
    def __init__(self, target: Path, force: bool, dry_run: bool):
        self.target = target
        self.force = force
        self.dry_run = dry_run
        self.counts = {"créé": 0, "mis à jour": 0, "ignoré": 0}

    # -- utilitaires -------------------------------------------------------------------
    def _log(self, action: str, path: Path, note: str = "") -> None:
        self.counts[action] += 1
        rel = path.relative_to(self.target)
        print(f"  {action:<11} {rel}{('  — ' + note) if note else ''}")

    def _write(self, path: Path, data: str | bytes, overwrite: bool, note: str = "") -> None:
        existed = path.exists()
        if existed and not overwrite:
            self._log("ignoré", path, note or "existe déjà (utiliser --force pour écraser)")
            return
        if not self.dry_run:
            path.parent.mkdir(parents=True, exist_ok=True)
            if isinstance(data, bytes):
                path.write_bytes(data)
            else:
                path.write_text(data, encoding="utf-8", newline="\n")
        self._log("mis à jour" if existed else "créé", path, note)

    # -- étapes ------------------------------------------------------------------------
    def copy_army(self) -> None:
        print("\n[1/6] Dossier army/")
        if self.target == SOURCE_ROOT:
            print("  (projet source : rien à copier)")
            return
        for src in sorted(ARMY.rglob("*")):
            if src.is_dir() or "__pycache__" in src.parts:
                continue
            dest = self.target / "army" / src.relative_to(ARMY)
            self._write(dest, src.read_bytes(), overwrite=self.force)

    def generate_agents(self) -> None:
        print(f"\n[2/6] Agents ({len(AGENTS)})")
        out = self.target / ".claude" / "agents"
        for agent in AGENTS:
            self._write(out / f"{agent[0]}.md", render_agent(*agent), overwrite=self.force)

    def copy_commands(self) -> None:
        print("\n[3/6] Commandes slash")
        out = self.target / ".claude" / "commands"
        for src in sorted((CLAUDE_TEMPLATES / "commands").glob("*.md")):
            self._write(out / src.name, src.read_text(encoding="utf-8"), overwrite=self.force)

    def merge_settings(self) -> None:
        print("\n[4/6] .claude/settings.json")
        path = self.target / ".claude" / "settings.json"
        template = json.loads((CLAUDE_TEMPLATES / "settings.json").read_text(encoding="utf-8"))
        if not path.exists():
            self._write(path, json.dumps(template, indent=2, ensure_ascii=False) + "\n", overwrite=False)
            return
        try:
            current = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            self._log("ignoré", path, f"JSON invalide, fusion impossible ({exc})")
            return
        current.setdefault("$schema", template["$schema"])
        perms = current.setdefault("permissions", {})
        added = 0
        for key, rules in template["permissions"].items():
            existing = perms.setdefault(key, [])
            for rule in rules:
                if rule not in existing:
                    existing.append(rule)
                    added += 1
        if added == 0:
            self._log("ignoré", path, "permissions déjà présentes")
            return
        self._write(path, json.dumps(current, indent=2, ensure_ascii=False) + "\n",
                    overwrite=True, note=f"{added} règle(s) de permission ajoutée(s)")

    def setup_claude_md(self) -> None:
        print("\n[5/6] CLAUDE.md")
        path = self.target / "CLAUDE.md"
        if not path.exists():
            self._write(path, (CLAUDE_TEMPLATES / "CLAUDE.template.md").read_text(encoding="utf-8"),
                        overwrite=False, note="à compléter (sections « À compléter »)")
            return
        content = path.read_text(encoding="utf-8")
        if ORCHESTRATOR_IMPORT in content:
            self._log("ignoré", path, "import de l'orchestrateur déjà présent")
            return
        addition = (
            "\n\n## Équipe d'agents\n"
            "Commandes : `/cadrage`, `/feature`, `/bugfix`, `/incident`, `/audit`, `/review`, `/adr`, `/status`.\n\n"
            f"{ORCHESTRATOR_IMPORT}\n"
        )
        self._write(path, content.rstrip() + addition, overwrite=True,
                    note="section « Équipe d'agents » ajoutée en fin de fichier")

    def update_gitignore(self) -> None:
        print("\n[6/6] .gitignore")
        path = self.target / ".gitignore"
        content = path.read_text(encoding="utf-8") if path.exists() else ""
        present = {line.strip() for line in content.splitlines()}
        missing = [line for line in GITIGNORE_LINES if line not in present]
        if not missing:
            self._log("ignoré", path, "entrées déjà présentes") if path.exists() else None
            return
        block = ("\n" if content and not content.endswith("\n") else "") + \
                ("\n" if content else "") + "# Claude Code (fichiers locaux)\n" + "\n".join(missing) + "\n"
        self._write(path, content + block, overwrite=True, note=", ".join(missing))

    def run(self) -> None:
        mode = " (simulation, rien n'est écrit)" if self.dry_run else ""
        print(f"Installation de l'équipe d'agents dans : {self.target}{mode}")
        self.copy_army()
        self.generate_agents()
        self.copy_commands()
        self.merge_settings()
        self.setup_claude_md()
        self.update_gitignore()
        c = self.counts
        print(f"\nTerminé : {c['créé']} créé(s), {c['mis à jour']} mis à jour, {c['ignoré']} ignoré(s).")
        if not self.dry_run:
            print("Étape suivante : compléter la section « Projet » du CLAUDE.md, puis ouvrir Claude Code dans ce dossier.")


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Installe l'équipe d'agents Claude Code dans un projet.")
    parser.add_argument("--target", type=Path, default=SOURCE_ROOT,
                        help="dossier du projet cible (défaut : le projet qui contient army/)")
    parser.add_argument("--force", action="store_true",
                        help="écrase les agents, commandes et fichiers army/ existants")
    parser.add_argument("--dry-run", action="store_true", help="affiche ce qui serait fait sans rien écrire")
    args = parser.parse_args()

    target = args.target.expanduser().resolve()
    if not target.is_dir():
        print(f"Erreur : le dossier cible n'existe pas : {target}", file=sys.stderr)
        return 1
    Installer(target, args.force, args.dry_run).run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
