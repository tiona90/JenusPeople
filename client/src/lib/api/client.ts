import axios, { isAxiosError } from 'axios'
import { getApiErrorMessage } from './error-utils'
import { emitApiError } from './error-events'
import { emitSessionExpired, isSessionExpiry } from './session-expiry'

export const apiBaseUrl =
    import.meta.env.VITE_API_BASE_URL?.trim() || '/api'

const apiClient = axios.create({
    baseURL: apiBaseUrl,
    withCredentials: true,
    headers: {
        'Content-Type': 'application/json',
    },
})

apiClient.interceptors.response.use(
    (response) => response,
    (error: unknown) => {
        if (isAxiosError(error)) {
            error.message = getApiErrorMessage(error, 'Request failed. Please try again.')

            const suppressGlobal = String(error.config?.headers?.['x-suppress-global-error'] ?? '').toLowerCase() === 'true'
            const status = error.response?.status
            const url = String(error.config?.url ?? '').toLowerCase()
            const isAuthBootstrap = status === 401 && (url.includes('/account/user-info') || url.includes('/account/currentuser'))
            const responseData = error.response?.data as { errors?: unknown } | undefined
            const hasValidationErrors = responseData?.errors != null
            const isValidationStatus = status === 400 || status === 422
            const isValidationError = isValidationStatus && hasValidationErrors

            // The cookie no longer signs anyone in: hand over to the app, which
            // signs the user out, rather than raising a snackbar per failed poll.
            if (!suppressGlobal && isSessionExpiry(status, url)) {
                emitSessionExpired()
            } else if (!suppressGlobal && !isAuthBootstrap && !isValidationError) {
                emitApiError({
                    message: error.message,
                    status,
                    url: error.config?.url,
                })
            }
        }

        return Promise.reject(error)
    }
)

export default apiClient