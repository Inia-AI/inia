/**
 * System-reserved placeholder names that cannot be used for variables or blocks
 */
export const RESERVED_PLACEHOLDER_NAMES = [
  'InputContext',
  'ReportDate',
  'OrganizationId',
  'Counterparty',
  'CounterpartyName',
  'CounterpartyId',
  'PeerCompanies',
  'PeerCompany',
] as const

/**
 * Prefixes that indicate system placeholders (case-insensitive)
 */
export const RESERVED_PLACEHOLDER_PREFIXES = [
  'Counterparty',
  'PeerCompan', // Matches PeerCompanies, PeerCompany
] as const

/**
 * Checks if a replacementTag conflicts with reserved system placeholder names
 * @param tag - The replacement tag to validate
 * @returns true if the tag is reserved/conflicts
 */
export function isReservedPlaceholder(tag: string): boolean {
  const normalizedTag = tag.toLowerCase()

  // Check exact matches (case-insensitive)
  if (RESERVED_PLACEHOLDER_NAMES.some(reserved => reserved.toLowerCase() === normalizedTag)) {
    return true
  }

  // Check prefixes (case-insensitive)
  if (RESERVED_PLACEHOLDER_PREFIXES.some(prefix => normalizedTag.startsWith(prefix.toLowerCase()))) {
    return true
  }

  return false
}

/**
 * Validates that a replacement tag doesn't conflict with system placeholders or existing tags
 * @param tag - The replacement tag to validate
 * @param existingTags - Array of existing tags to check against (variables or blocks)
 * @param type - Type of entity being validated (for error messages)
 * @returns Validation result with error message if invalid
 */
export function validateReplacementTag(
  tag: string,
  existingTags: string[],
  type: 'variable' | 'block'
): { isValid: boolean; error?: string } {
  // 1. Check if empty
  if (!tag || tag.trim() === '') {
    return {
      isValid: false,
      error: 'Replacement tag cannot be empty'
    }
  }

  // 2. Check if it's a reserved system placeholder
  if (isReservedPlaceholder(tag)) {
    return {
      isValid: false,
      error: `'${tag}' is a reserved system placeholder and cannot be used for ${type}s`
    }
  }

  // 3. Check for conflicts with existing tags (case-insensitive)
  const normalizedTag = tag.toLowerCase()
  const conflict = existingTags.find(existing => existing.toLowerCase() === normalizedTag)

  if (conflict) {
    return {
      isValid: false,
      error: `A ${type} with replacement tag '${conflict}' already exists`
    }
  }

  // 4. Check format (alphanumeric, underscore, hyphen only)
  const validFormat = /^[A-Za-z][A-Za-z0-9_-]*$/
  if (!validFormat.test(tag)) {
    return {
      isValid: false,
      error: 'Replacement tag must start with a letter and contain only letters, numbers, underscores, and hyphens'
    }
  }

  return { isValid: true }
}

/**
 * Validates that a block's replacement tag doesn't conflict with variables or system placeholders
 * @param blockTag - The block replacement tag to validate
 * @param variableTags - Array of existing variable replacement tags
 * @param blockTags - Array of existing block replacement tags (excluding the current block)
 * @returns Validation result with error message if invalid
 */
export function validateBlockReplacementTag(
  blockTag: string,
  variableTags: string[],
  blockTags: string[]
): { isValid: boolean; error?: string } {
  // First check against system placeholders and format
  const basicValidation = validateReplacementTag(blockTag, blockTags, 'block')
  if (!basicValidation.isValid) {
    return basicValidation
  }

  // Additionally check against variable tags (blocks can't have same name as variables)
  const normalizedBlockTag = blockTag.toLowerCase()
  const variableConflict = variableTags.find(varTag => varTag.toLowerCase() === normalizedBlockTag)

  if (variableConflict) {
    return {
      isValid: false,
      error: `A variable with replacement tag '${variableConflict}' already exists. Blocks and variables cannot share the same replacement tag.`
    }
  }

  return { isValid: true }
}

/**
 * Validates that a variable's replacement tag doesn't conflict with system placeholders
 * @param variableTag - The variable replacement tag to validate
 * @param existingVariableTags - Array of existing variable replacement tags (excluding the current variable)
 * @returns Validation result with error message if invalid
 */
export function validateVariableReplacementTag(
  variableTag: string,
  existingVariableTags: string[]
): { isValid: boolean; error?: string } {
  return validateReplacementTag(variableTag, existingVariableTags, 'variable')
}
