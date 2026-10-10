/** Smallest password the server takes (PasswordStore.MinLength). */
export const MIN_PASSWORD = 8

/** What keeps the setup form from being sent, or undefined when it can go. */
export function setupProblem(code: string, password: string, repeat: string): string | undefined {
  if (code.replace(/[\s-]/g, '').length !== 8) return 'The setup code has 8 characters, like ABCD-EFGH'
  if (password.length < MIN_PASSWORD) return `The password needs at least ${MIN_PASSWORD} characters`
  if (password !== repeat) return "The passwords don't match"
  return undefined
}
