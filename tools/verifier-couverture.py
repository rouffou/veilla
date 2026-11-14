#!/usr/bin/env python3
"""Seuil de couverture bloquant sur Domain et Application (ARC-23).

Lit les rapports Cobertura d'une solution (un par projet de tests), fusionne les lignes (une ligne est couverte si au moins un
projet de tests l'exerce) et calcule, pour la solution d'un service, la couverture des assemblies `Sepp.<Service>.Domain` et
`Sepp.<Service>.Application`. Échoue si une couche passe sous son seuil.

Seuils : `tools/couverture-seuils.json`. La cible est 80 %. Un service sous la cible porte un seuil propre, au niveau actuel
(cliquet) : il ne peut que monter. Un service absent du fichier est tenu à la cible.

Usage : python3 tools/verifier-couverture.py --solution Affilies --resultats TestResults [--resume "$GITHUB_STEP_SUMMARY"]
"""
import argparse
import glob
import json
import math
import os
import sys
import xml.etree.ElementTree as ET

COUCHES = ("Domain", "Application")


def lire_lignes(fichiers, prefixe):
    """Retourne {couche: {(fichier, ligne): max des passages}} pour les packages `Sepp.<prefixe>.<couche>`."""
    lignes = {couche: {} for couche in COUCHES}
    for chemin in fichiers:
        racine = ET.parse(chemin).getroot()
        for package in racine.iter("package"):
            nom = package.get("name", "")
            for couche in COUCHES:
                if nom != f"Sepp.{prefixe}.{couche}":
                    continue
                for classe in package.iter("class"):
                    fichier = classe.get("filename", "")
                    for ligne in classe.iter("line"):
                        cle = (fichier, int(ligne.get("number")))
                        lignes[couche][cle] = max(lignes[couche].get(cle, 0), int(ligne.get("hits")))
    return lignes


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parseur = argparse.ArgumentParser()
    parseur.add_argument("--solution", required=True, help="Nom de la solution (Affilies, PostesRisques, Socle…)")
    parseur.add_argument("--resultats", required=True, help="Dossier des résultats de tests (rapports *.cobertura.xml)")
    parseur.add_argument("--seuils", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "couverture-seuils.json"))
    parseur.add_argument("--resume", help="Fichier Markdown auquel ajouter le tableau (GITHUB_STEP_SUMMARY)")
    args = parseur.parse_args()

    with open(args.seuils, encoding="utf-8") as f:
        config = json.load(f)
    cible = config["cible"]
    prefixe = config.get("prefixes", {}).get(args.solution, args.solution)
    seuils = config["seuils"].get(args.solution, {})

    fichiers = glob.glob(os.path.join(args.resultats, "**", "*.cobertura.xml"), recursive=True)
    if not fichiers:
        print(f"::error::Aucun rapport de couverture dans {args.resultats}.")
        return 1

    lignes = lire_lignes(fichiers, prefixe)
    echecs = []
    rapport = [
        f"### Seuil de couverture — {args.solution}",
        "",
        "| Couche | Couverture | Seuil | Cible | Verdict |",
        "|---|---:|---:|---:|---|",
    ]
    for couche in COUCHES:
        valides = len(lignes[couche])
        if valides == 0:
            rapport.append(f"| {couche} | — | — | {cible} % | aucune ligne (couche absente) |")
            continue
        couvertes = sum(1 for hits in lignes[couche].values() if hits > 0)
        pourcentage = 100.0 * couvertes / valides
        seuil = seuils.get(couche.lower(), cible)
        if pourcentage + 1e-9 < seuil:
            verdict = "ÉCHEC : sous le seuil"
            echecs.append(f"{args.solution} {couche} : {pourcentage:.1f} % < seuil {seuil} %")
        elif seuil < cible:
            verdict = f"ok (cliquet, écart à la cible : {cible - pourcentage:.1f} pt)"
            if math.floor(pourcentage) - 1 > seuil:
                verdict += f" ; relever le seuil à {math.floor(pourcentage) - 1}"
        else:
            verdict = "ok"
        rapport.append(f"| {couche} | {pourcentage:.1f} % ({couvertes}/{valides}) | {seuil} % | {cible} % | {verdict} |")

    texte = "\n".join(rapport)
    print(texte)
    if args.resume:
        with open(args.resume, "a", encoding="utf-8") as f:
            f.write(texte + "\n")
    for echec in echecs:
        print(f"::error::Couverture insuffisante, {echec}. Ajouter des tests (le seuil ne se baisse jamais).")
    return 1 if echecs else 0


if __name__ == "__main__":
    sys.exit(main())
