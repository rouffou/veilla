#!/usr/bin/env python3
"""Tests de `verifier-couverture.py` et `verifier-cliquet.py` (#479). Lancement : python3 tools/test_verifier_couverture.py"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest

OUTILS = os.path.dirname(os.path.abspath(__file__))


def cobertura(packages):
    """Rapport Cobertura minimal : packages = {nom: [(numero, hits)]}."""
    corps = ""
    for nom, lignes in packages.items():
        xml = "".join(f'<line number="{n}" hits="{h}"/>' for n, h in lignes)
        corps += f'<package name="{nom}"><classes><class filename="{nom}.cs"><lines>{xml}</lines></class></classes></package>'
    return f'<?xml version="1.0"?><coverage><packages>{corps}</packages></coverage>'


def lancer(script, *args):
    return subprocess.run([sys.executable, os.path.join(OUTILS, script), *args], capture_output=True, text=True, encoding="utf-8")


class VerifierCouverture(unittest.TestCase):
    def setUp(self):
        self.dossier = tempfile.TemporaryDirectory()
        self.addCleanup(self.dossier.cleanup)

    def ecrire_rapport(self, packages):
        with open(os.path.join(self.dossier.name, "a.cobertura.xml"), "w", encoding="utf-8") as f:
            f.write(cobertura(packages))

    def seuils(self):
        chemin = os.path.join(self.dossier.name, "seuils.json")
        with open(chemin, "w", encoding="utf-8") as f:
            json.dump({"cible": 80, "seuils": {}, "sans_couches": ["Sagas"]}, f)
        return chemin

    def verifier(self, solution):
        return lancer("verifier-couverture.py", "--solution", solution, "--resultats", self.dossier.name, "--seuils", self.seuils())

    def test_couche_absente_echoue(self):
        # Reproduction du P1-1 : les rapports ne contiennent pas la solution demandée.
        self.ecrire_rapport({"Sepp.BuildingBlocks.Domain": [(1, 1), (2, 1)], "Sepp.BuildingBlocks.Application": [(1, 1)]})
        resultat = self.verifier("Affilies")
        self.assertEqual(resultat.returncode, 1, resultat.stdout)
        self.assertIn("aucune ligne mesurée", resultat.stdout)

    def test_une_seule_couche_absente_echoue(self):
        self.ecrire_rapport({"Sepp.Affilies.Domain": [(1, 1)]})
        self.assertEqual(self.verifier("Affilies").returncode, 1)

    def test_solution_sans_couches_est_dispensee(self):
        self.ecrire_rapport({"Sepp.Autre.Domain": [(1, 1)]})
        resultat = self.verifier("Sagas")
        self.assertEqual(resultat.returncode, 0, resultat.stdout)

    def test_couches_presentes_au_dessus_du_seuil(self):
        self.ecrire_rapport({"Sepp.Affilies.Domain": [(1, 1)], "Sepp.Affilies.Application": [(1, 1)]})
        self.assertEqual(self.verifier("Affilies").returncode, 0)

    def test_couche_sous_le_seuil_echoue(self):
        self.ecrire_rapport({"Sepp.Affilies.Domain": [(1, 1), (2, 0)], "Sepp.Affilies.Application": [(1, 1)]})
        self.assertEqual(self.verifier("Affilies").returncode, 1)

    def test_aucun_rapport_echoue(self):
        self.assertEqual(self.verifier("Affilies").returncode, 1)


class VerifierCliquet(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location("cliquet", os.path.join(OUTILS, "verifier-cliquet.py"))
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)

    def test_baisse_detectee(self):
        base = {"cible": 80, "seuils": {"X": {"domain": 70}}}
        self.assertEqual(len(self.module.regressions(base, {"cible": 80, "seuils": {"X": {"domain": 65}}})), 1)

    def test_nouvelle_entree_sous_la_cible_detectee(self):
        base = {"cible": 80, "seuils": {}}
        self.assertEqual(len(self.module.regressions(base, {"cible": 80, "seuils": {"Y": {"application": 50}}})), 1)

    def test_baisse_de_la_cible_detectee(self):
        self.assertTrue(self.module.regressions({"cible": 80, "seuils": {}}, {"cible": 70, "seuils": {}}))

    def test_hausse_et_retrait_permis(self):
        base = {"cible": 80, "seuils": {"X": {"domain": 70}, "Z": {"application": 71}}}
        courant = {"cible": 80, "seuils": {"X": {"domain": 75}}}
        self.assertEqual(self.module.regressions(base, courant), [])


if __name__ == "__main__":
    unittest.main()
