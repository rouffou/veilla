# -*- coding: utf-8 -*-
import json, os, subprocess, sys, time, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(__file__))
from data import *

REPO = sys.argv[1]  # owner/name
OWNER_LOGIN = REPO.split("/")[0]
STATE_FILE = os.path.join(os.path.dirname(__file__), "state.json")
TOKEN = subprocess.check_output(["gh", "auth", "token"], text=True).strip()
state = json.load(open(STATE_FILE, encoding="utf-8")) if os.path.exists(STATE_FILE) else {}
state.setdefault("issues", {}); state.setdefault("milestones", {}); state.setdefault("subs", []); state.setdefault("project_items", [])

def save():
    json.dump(state, open(STATE_FILE, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

def api(method, path, body=None, tries=6):
    url = path if path.startswith("http") else "https://api.github.com" + path
    data = json.dumps(body).encode() if body is not None else None
    for attempt in range(tries):
        req = urllib.request.Request(url, data=data, method=method, headers={
            "Authorization": f"Bearer {TOKEN}", "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28", "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req) as r:
                txt = r.read().decode()
                return json.loads(txt) if txt else {}
        except urllib.error.HTTPError as e:
            msg = e.read().decode()
            if e.code in (403, 429) and ("rate limit" in msg.lower() or "abuse" in msg.lower() or "secondary" in msg.lower()):
                wait = int(e.headers.get("Retry-After") or 60)
                print(f"  rate limited, waiting {wait}s"); time.sleep(wait); continue
            if e.code >= 500 and attempt < tries - 1:
                time.sleep(5); continue
            raise RuntimeError(f"{method} {path} -> {e.code}: {msg}")

def gql(query, variables):
    r = api("POST", "/graphql", {"query": query, "variables": variables})
    if "errors" in r:
        raise RuntimeError(r["errors"])
    return r["data"]

# ---------- Labels ----------
TYPE_COLORS = {
    "epic": "5319e7", "exigence-fonctionnelle": "1d76db", "exigence-non-fonctionnelle": "fbca04",
    "architecture": "0e8a16", "infrastructure": "006b75", "donnees": "c5def5", "interoperabilite": "bfd4f2",
    "migration": "d4c5f9", "recette": "f9d0c4", "formation": "fef2c0", "maintenance": "e4e669",
    "tache-technique": "bfdadc",
}
def ensure_labels():
    existing = {l["name"] for page in range(1, 5) for l in api("GET", f"/repos/{REPO}/labels?per_page=100&page={page}")}
    wanted = [(f"type:{k}", v, f"Type : {k}") for k, v in TYPE_COLORS.items()]
    for k, (name, zone, desc) in SERVICES.items():
        wanted.append((f"svc:{k}", "ededed", f"Service : {name}"[:100]))
    wanted += [("zone:medicale", "b60205", "Zone médicale – secret médical (ARC-04)"),
               ("zone:psychosociale", "d93f0b", "Zone psychosociale – accès CPAP (ARC-04)")]
    for name, color, desc in wanted:
        if name not in existing:
            api("POST", f"/repos/{REPO}/labels", {"name": name, "color": color, "description": desc})
            print("label", name)

# ---------- Milestones ----------
def ensure_milestones():
    existing = {m["title"]: m["number"] for m in api("GET", f"/repos/{REPO}/milestones?state=all&per_page=100")}
    for title, desc in MILESTONES:
        if title not in existing:
            existing[title] = api("POST", f"/repos/{REPO}/milestones", {"title": title, "description": desc})["number"]
            print("milestone", title)
        state["milestones"][title] = existing[title]
    save()

def zone_labels(svcs):
    z = set()
    for s in svcs:
        zone = SERVICES[s][1]
        if zone in ("medicale", "psychosociale"):
            z.add(f"zone:{zone}")
    return sorted(z)

def create_issue(key, title, body, labels, milestone):
    if key in state["issues"]:
        return state["issues"][key]
    r = api("POST", f"/repos/{REPO}/issues", {"title": title, "body": body, "labels": labels,
                                              "milestone": state["milestones"][milestone]})
    state["issues"][key] = {"number": r["number"], "id": r["id"], "node_id": r["node_id"]}
    save()
    print(f"#{r['number']} {title}")
    time.sleep(1.2)
    return state["issues"][key]

DOD = """### Définition de terminé
- [ ] Conception validée (cas d'usage, contrats d'API / événements)
- [ ] Implémentation dans le service propriétaire (clean architecture, ARC-03)
- [ ] Règles légales paramétrables via Référentiels le cas échéant (ARC-21, NF-61)
- [ ] Droits d'accès conformes à la matrice §3.3 et journalisation (NF-04)
- [ ] Tests automatisés (unitaires, intégration, contrat) — ARC-23
- [ ] Scénario de recette rattaché à l'identifiant de l'exigence — REC-01"""

def req_body(r, svc, extra_svcs, lot):
    contrib = ", ".join(SERVICES[s][0] for s in extra_svcs) or "—"
    return f"""**{r['id']}** — Cahier des charges SEPP v1.1, §{r['section']}

> {r['text']}

| Service propriétaire | Contributeurs | Zone | Lot |
|---|---|---|---|
| {SERVICES[svc][0]} | {contrib} | {SERVICES[svc][1]} | {lot} |

{DOD}
"""

def main():
    ensure_labels()
    ensure_milestones()

    # Epics
    epic_lot = {}
    for svc in SERVICES:
        ids = [r["id"] for r in REQS + EXTRAS if OWNER.get(r["id"]) == svc]
        lots = sorted({lot_of(i) for i in ids}) or [SVC_LOT[svc]]
        epic_lot[svc] = SVC_LOT[svc]
        name, zone, desc = SERVICES[svc]
        body = f"""## Epic — {name}

{desc}

**Zone de sensibilité** : {zone}
**Exigences couvertes** : {", ".join(ids)}
**Lots concernés** : {"; ".join(lots)}

Les exigences sont suivies en sous-issues de cet epic (matrice de traçabilité ARC-09, §14.4).
"""
        create_issue(f"EPIC-{svc}", f"[Epic] {name}", body, ["type:epic", f"svc:{svc}"] + zone_labels([svc]), SVC_LOT[svc])

    # Requirements + extras
    for r in REQS + EXTRAS:
        rid = r["id"]; svc = OWNER[rid]; extra = EXTRA_SVC.get(rid, []); lot = lot_of(rid)
        labels = [f"type:{type_of(rid)}", f"svc:{svc}"] + [f"svc:{s}" for s in extra] + zone_labels([svc] + extra)
        create_issue(rid, f"[{rid}] {r['title']}", req_body(r, svc, extra, lot), labels, lot)

    # Schema tasks (§15.3)
    for svc, entities in SCHEMAS.items():
        key = f"SCH-{svc}"
        body = f"""Schéma de données initial du service **{SERVICES[svc][0]}** (§15.3).

**Entités** : {entities}

- [ ] Migrations versionnées dans le dépôt du service (DAT-09)
- [ ] Identifiants UUID v7 (DAT-01), références inter-services par identifiant (DAT-02)
- [ ] Colonnes d'audit + verrou optimiste (DAT-03), validité (DAT-04), suppression logique (DAT-05)
- [ ] Champs sensibles chiffrés au niveau applicatif le cas échéant (ARC-45)
"""
        create_issue(key, f"[Données] Schéma initial – {SERVICES[svc][0]}", body,
                     ["type:donnees", f"svc:{svc}"] + zone_labels([svc]), SVC_LOT[svc])
        OWNER[key] = svc

    # Sub-issues
    done = set(state["subs"])
    for key, info in list(state["issues"].items()):
        if key.startswith("EPIC-") or key in done:
            continue
        svc = OWNER[key]
        epic = state["issues"][f"EPIC-{svc}"]
        try:
            api("POST", f"/repos/{REPO}/issues/{epic['number']}/sub_issues", {"sub_issue_id": info["id"]})
        except RuntimeError as e:
            if "already" not in str(e).lower():
                raise
        state["subs"].append(key); save()
        time.sleep(0.3)
    print("sub-issues linked")

    # Project v2
    if "project_id" not in state:
        owner_id = gql("query($l:String!){user(login:$l){id}}", {"l": OWNER_LOGIN})["user"]["id"]
        p = gql("mutation($o:ID!,$t:String!){createProjectV2(input:{ownerId:$o,title:$t}){projectV2{id number url}}}",
                {"o": owner_id, "t": "SEPP – Logiciel métier"})["createProjectV2"]["projectV2"]
        state["project_id"] = p["id"]; state["project_url"] = p["url"]; save()
        repo_id = api("GET", f"/repos/{REPO}")["node_id"]
        gql("mutation($p:ID!,$r:ID!){linkProjectV2ToRepository(input:{projectId:$p,repositoryId:$r}){clientMutationId}}",
            {"p": p["id"], "r": repo_id})
    added = set(state["project_items"])
    for key, info in state["issues"].items():
        if key in added:
            continue
        gql("mutation($p:ID!,$c:ID!){addProjectV2ItemById(input:{projectId:$p,contentId:$c}){item{id}}}",
            {"p": state["project_id"], "c": info["node_id"]})
        state["project_items"].append(key); save()
        time.sleep(0.3)
    print("project:", state["project_url"])
    print("total issues:", len(state["issues"]))

main()
