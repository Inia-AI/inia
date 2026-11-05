import { z } from 'zod'
import {
  COUNTERPARTY_NAME_MAX_LENGTH,
  isValidCounterpartyName,
  isValidCounterpartyNameVariants,
} from '@/utils/counterpartyName'

export const blankToNull = (value: unknown): string | null => {
  if (value == null) return null
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

const emailValidator = z.string().email()
const urlValidator = z.string().url()

const isBlankOrValid = (
  value: string | null | undefined,
  validator: z.ZodString,
) => {
  if (value == null || value.trim() === '') return true
  return validator.safeParse(value.trim()).success
}

export const buildCounterpartyFormSchema = (t: (key: string) => string) =>
  z.object({
    id: z
      .string()
      .min(
        3,
        t(
          'shared.components.counterpartySelect.referenceId.validation.minLength',
        ),
      ),
    name: z
      .string()
      .min(1, t('page.counterpartyManagement.form.validation.nameRequired'))
      .max(COUNTERPARTY_NAME_MAX_LENGTH, t('page.counterpartyManagement.form.validation.nameTooLong'))
      .refine(isValidCounterpartyName, {
        message: t(
          'page.counterpartyManagement.form.validation.nameForbiddenCharacters',
        ),
      }),
    nameVariants: z
      .string()
      .optional()
      .refine(isValidCounterpartyNameVariants, {
        message: t(
          'page.counterpartyManagement.form.validation.nameVariantsForbiddenCharacters',
        ),
      }),
    email: z
      .string()
      .nullish()
      .refine(v => isBlankOrValid(v, emailValidator), {
        message: t('page.counterpartyManagement.form.validation.invalidEmail'),
      }),
    website: z
      .string()
      .nullish()
      .refine(v => isBlankOrValid(v, urlValidator), {
        message: t('page.counterpartyManagement.form.validation.invalidUrl'),
      }),
    lastFinancialAnalysisResult: z.string().optional(),
    counterpartyTypeIds: z
      .array(z.string())
      .min(1, t('page.counterpartyManagement.form.validation.typeRequired')),
  })
