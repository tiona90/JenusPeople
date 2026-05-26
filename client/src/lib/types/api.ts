export interface ApiMessageResponse {
    message: string;
    emailVerificationRequired?: boolean;
    verificationEmailSent?: boolean;
    [key: string]: unknown;
}

export interface ApiErrorResponse {
    statusCode?: number
    message?: string
    path?: string
    traceId?: string
    timestamp?: string
    details?: string
    errors?: string[] | Record<string, string[]>
}
