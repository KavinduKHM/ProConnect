export interface User {
  id: string;
  email: string;
  fullName: string;
  isVendor: boolean;
  phoneNumber?: string;
  companyName?: string;
  role?: string; // Admin, Vendor, Customer
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  fullName: string;
  role: 'Customer' | 'Vendor';
  companyName?: string;
  phoneNumber?: string;
  address?: string;
}

export interface AuthResponse {
  message: string;
  token: string;
  user: User;
}