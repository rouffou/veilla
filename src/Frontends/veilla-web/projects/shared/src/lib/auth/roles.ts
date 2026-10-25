/**
 * Rôles portés par le claim `roles` (rôles de realm Keycloak, mappeur « roles » du scope `sepp-api` : jeton d'accès,
 * jeton d'identité et userinfo). Le claim est un tableau ; une chaîne seule est tolérée. À défaut, on lit
 * `realm_access.roles` (format Keycloak par défaut).
 */
export function extraireRoles(source: unknown): readonly string[] {
  if (!source || typeof source !== 'object') return [];
  const donnees = source as Record<string, unknown>;
  const brut = donnees['roles'] ?? (donnees['realm_access'] as Record<string, unknown> | undefined)?.['roles'];
  if (typeof brut === 'string') return [brut];
  return Array.isArray(brut) ? brut.filter((r): r is string => typeof r === 'string') : [];
}

/** Vrai si aucun rôle n'est exigé, ou si l'utilisateur en possède au moins un parmi `exiges`. */
export function possedeUnRole(roles: readonly string[], exiges: readonly string[] | undefined): boolean {
  return !exiges || exiges.length === 0 || exiges.some((r) => roles.includes(r));
}
