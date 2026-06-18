INSERT INTO users (username, password_hash, display_name, role, is_active)
VALUES
    ('appadmin', crypt('Password123!', gen_salt('bf', 10)), 'Application Admin', 'ApplicationAdmin', TRUE),
    ('devops', crypt('Password123!', gen_salt('bf', 10)), 'DevOps User', 'DevOps', TRUE),
    ('selleradmin1', crypt('Password123!', gen_salt('bf', 10)), 'Seller Admin One', 'SellerAdmin', TRUE),
    ('buyer1', crypt('Password123!', gen_salt('bf', 10)), 'Buyer One', 'Buyer', TRUE),
    ('buyer2', crypt('Password123!', gen_salt('bf', 10)), 'Buyer Two', 'Buyer', TRUE)
ON CONFLICT (username) DO NOTHING;