#!/usr/bin/env python3
"""Cliquet des seuils de couverture (ARC-23, #479) : aucun seuil de `couverture-seuils.json` ne peut baisser.

Compare le fichier de la PR à celui de `main` (`--base`). Le seuil effectif d'une couche est son seuil propre, sinon la cible :
retirer une entrée relève donc le seuil (jusqu'à la cible) et reste permis ; baisser un seuil, ajouter une entrée sous la cible
pour un service jusque-là tenu à la cible, ou baisser la cible échoue.

Usage : python3 tools/verifier-cliquet.py --base <couverture-seuils.json de main> [--courant tools/couverture-seuils.json]
"""
import argparse
import json
import os
import sys

COUCHES = ("domain", "application")


def seuil_effectif(config, solution, couche):
    return config.get("seuils", {}).get(solution, {}).get(couche, config["cible"])


def regressions(base, courant):
    """Liste les baisses de seuil entre `base` et `courant` (deux configurations chargées)."""
    resultat = []
    if courant["cible"] < base["cible"]:
        resultat.append(f"cible : {base['cible']} % -> {courant['cible']} %")
    solutions = set(base.get("seuils", {})) | set(courant.get("seuils", {}))
    for solution in sorted(solutions):
        for couche in COUCHES:
            avant = seuil_effectif(base, solution, couche)
            apres = seuil_effectif(courant, solution, couche)
            if apres < avant:
                resultat.append(f"{solution} {couche} : {avant} % -> {apres} %")
    return resultat


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parseur = argparse.ArgumentParser()
    parseur.add_argument("--base", required=True, help="couverture-seuils.json de la branche de base (main)")
    parseur.add_argument("--courant", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "couverture-seuils.json"))
    args = parseur.parse_args()
    with open(args.base, encoding="utf-8") as f:
        base = json.load(f)
    with open(args.courant, encoding="utf-8") as f:
        courant = json.load(f)
    baisses = regressions(base, courant)
    for baisse in baisses:
        print(f"::error::Seuil de couverture abaissé ({baisse}). Un seuil ne se baisse jamais : ajouter des tests à la place.")
    if not baisses:
        print("Aucun seuil de couverture n'a baissé par rapport à la base.")
    return 1 if baisses else 0


if __name__ == "__main__":
    sys.exit(main())
