"use client";

import Image from 'next/image';
import Link from 'next/link';
import { useState, useEffect } from 'react';
import { readItems } from '@directus/sdk';
import client from '../../lib/directus';

export default function Posts({ tagline, headline, limit = 6 }) {

    const [posts, setPosts] = useState(null);

    useEffect(() => {
        async function fetchPosts() {
            const data = await client.request(
                readItems('posts', {
                    limit,
                    fields: ['id', 'title', 'slug', {'author': ["first_name", "last_name"]}, 'published_at', 'image.title', 'image.id', 'description'],
                    filter: { published_at: { _empty: false } },
                })
            );
            setPosts(data);
        }

        fetchPosts();
    }, [limit])

    console.log('posts', posts);

    return (
        <section className="posts-section">
            <p>{tagline}</p>
            <h1>{headline}</h1>
            <div className="posts-container">
            {posts ? (
                <div className="posts-grid">
                    {posts.map((post) => (
                        <div key={post.id}>
                            <Post {...post} />
                        </div>
                    ))}
                </div>
            ) : (
                <div>Loading...</div>
            )}
            </div>
            <style jsx>{`
                .posts-section {
                    padding: 40px;
                }
                .posts-container {
                    display: flex;
                    flex-wrap: wrap;
                    gap: 3rem;
                }
                .posts-grid {
                    display: flex;
                    gap: 1rem;
                    width: 100%;
                    flex-direction: row;
                    justify-content: flex-start;
                }                    
                h1 {
                    font-size: 48px;
                    font-weight: bold;
                    margin-bottom: 20px;
                    color: #1a202c;
                    margin-left: 20px;
                }
                p {
                    margin-left: 20px;
                    margin-top: 20px;
                }
            `}</style>
        </section>
    );
}

function Post({ id, title, author, slug, description, image, content, published_at }) {
    return (
        <div className={'card'}>
            <Image
                src={`http://localhost:8055/assets/${image.id}`}
                alt={image.title}
                width={400}
                height={300}
                className="gallery-image"
            />
            <h2>{title}</h2>
            <p className="author">by {author.first_name} {author.last_name}</p>
            {content ? (
                <>
                    <p>Published on {new Date(published_at).toDateString()}</p>
                    <hr />
                    <div dangerouslySetInnerHTML={{ __html: content }} />
                </>
            ) : (
                <div>
                    <p>{description}</p>
                    <Link href={`/posts/${slug}`}><div className="link">Read more</div></Link>
                </div>
            )}

            <style jsx>{`
                .card {
                    border-radius: 8px;
                    box-shadow: 0 4px 8px rgba(0, 0, 0, 0.1);
                    overflow: hidden;
                    transition: transform 0.3s ease;
                    background-color: #fff;
                    max-width: 400px;
                    height: 580px;
                    margin: 1rem;
                    position: relative;
                }
                .card:hover {
                    box-shadow: 0 6px 12px rgba(0, 0, 0, 0.15);
                }
                .card img {
                    width: 100%;
                    height: 200px;
                    object-fit: cover;
                    display: block;
                }
                .article img {
                    width: 100%;
                    height: 500px;
                    object-fit: cover;
                    display: block;
                }
                .article .author {
                    font-style: italic;
                }
                .card h2 {
                    margin: 1rem;
                    font-size: 1.5rem;
                    color: #333;
                }
                .card p {
                    margin: 0 1rem 1rem;
                    color: #666;
                    line-height: 1.4;
                    text-overflow: ellipsis;
                    display: box;
                    overflow: hidden;
                    max-height: 4rem;
                }
                .link {
                    margin: 1rem;
                    color: #0000EE;
                }
            `}</style>
        </div>
    );
}